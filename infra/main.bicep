// Earthquake Story Machine — full infrastructure.
// Cost floor by design: every SKU below is the cheapest tier that works, so an
// idle month costs ~$0. Upgrading any SKU requires explicit approval (see infra-spec).
//
// Deploy (only on explicit user request — out of sprint scope):
//   az group create -n rg-quakestory -l <location>
//   az deployment group create -g rg-quakestory -f infra/main.bicep -p infra/main.bicepparam

targetScope = 'resourceGroup'

@description('Azure region for all resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Unsplash API access key. Supplied at deploy time / via GitHub secret — never a literal in the repo.')
@secure()
param unsplashAccessKey string

@description('Administrator login for the Azure SQL server.')
param sqlAdminLogin string = 'quakeadmin'

@description('Administrator password for the Azure SQL server. Supplied via GitHub secret.')
@secure()
param sqlAdminPassword string

@description('Minimum earthquake magnitude the poller publishes. Mirrors UsgsMinMagnitude in local.settings.json.')
param usgsMinMagnitude string = '4.5'

@description('NCRONTAB schedule for the USGS poller. Mirrors UsgsPollSchedule in local.settings.json.')
param usgsPollSchedule string = '0 */5 * * * *'

var uniqueSuffix = uniqueString(resourceGroup().id)
// Storage account names are capped at 24 chars and allow no hyphens; 'stquakestory' (12) +
// the full 13-char uniqueString would be 25. Trim to fit.
var storageSuffix = take(uniqueSuffix, 11)
var tags = {
  project: 'earthquake-story-machine'
  environment: 'portfolio'
}

// ---------------------------------------------------------------------------
// Service Bus — Basic SKU (queues only, cheapest). Queue: quake-events.
// ---------------------------------------------------------------------------
resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sb-quakestory-${uniqueSuffix}'
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource quakeEventsQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: 'quake-events'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT5M'
    defaultMessageTimeToLive: 'P14D'
  }
}

// RootManage rule exists on the namespace by default; reference it for the connection string.
resource sbAuthRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2022-10-01-preview' existing = {
  parent: serviceBus
  name: 'RootManageSharedAccessKey'
}

// ---------------------------------------------------------------------------
// Storage — Standard_LRS. Container: story-cards. Also backs the Function App.
// ---------------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: 'stquakestory${storageSuffix}'
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storage
  name: 'default'
}

resource storyCardsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'story-cards'
  properties: {
    publicAccess: 'None'
  }
}

// ---------------------------------------------------------------------------
// Application Insights (workspace-based) + Log Analytics workspace.
// ---------------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'log-quakestory-${uniqueSuffix}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-quakestory'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------------------------------------------------------------------------
// Azure SQL — serverless GP_S_Gen5_1 with 60-min auto-pause (scales to zero when idle).
// ---------------------------------------------------------------------------
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: 'sql-quakestory-${uniqueSuffix}'
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: 'QuakeDb'
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5_1'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 2147483648
    zoneRedundant: false
  }
}

// Allow other Azure services (the Function App) to reach SQL.
resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAllAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ---------------------------------------------------------------------------
// Function App — Y1 consumption, dotnet-isolated net8.0.
// App settings mirror src/Quake.Functions/local.settings.json 1:1 (boundary B4).
// ---------------------------------------------------------------------------
resource hostingPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-quakestory-${uniqueSuffix}'
  location: location
  tags: tags
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
}

var storageConnection = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'
var serviceBusConnection = sbAuthRule.listKeys().primaryConnectionString
var sqlConnection = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=QuakeDb;Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: 'func-quakestory-${uniqueSuffix}'
  location: location
  tags: tags
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    siteConfig: {
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      netFrameworkVersion: 'v8.0'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: storageConnection
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: storageConnection
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: toLower('func-quakestory-${uniqueSuffix}')
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        // --- The app-specific keys the Functions code reads (boundary B4). Names are load-bearing. ---
        // ServiceBusConnection and UsgsPollSchedule are consumed as trigger binding expressions
        // (Connection = "ServiceBusConnection", schedule "%UsgsPollSchedule%"); the rest via
        // IConfiguration. All must mirror src/Quake.Functions/local.settings.json exactly.
        {
          name: 'ServiceBusConnection'
          value: serviceBusConnection
        }
        {
          name: 'BlobStorageConnection'
          value: storageConnection
        }
        {
          name: 'SqlConnection'
          value: sqlConnection
        }
        {
          name: 'UnsplashAccessKey'
          value: unsplashAccessKey
        }
        {
          name: 'UsgsMinMagnitude'
          value: usgsMinMagnitude
        }
        {
          name: 'UsgsPollSchedule'
          value: usgsPollSchedule
        }
      ]
    }
  }
}

// ---------------------------------------------------------------------------
// Static Web App — Free SKU. Frontend; /api/* links to the Function App backend.
// ---------------------------------------------------------------------------
resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: 'swa-quakestory'
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------
output functionAppName string = functionApp.name
output functionAppHostName string = functionApp.properties.defaultHostName
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output staticWebAppName string = staticWebApp.name
output staticWebAppDefaultHostName string = staticWebApp.properties.defaultHostname
output serviceBusNamespace string = serviceBus.name
output storageAccountName string = storage.name
