using './main.bicep'

// Secrets are read from environment variables at deploy time, never stored in the repo.
// In CI/deploy, these come from GitHub secrets (see .github/workflows/deploy.yml):
//   UNSPLASH_ACCESS_KEY, SQL_ADMIN_PASSWORD.
param unsplashAccessKey = readEnvironmentVariable('UNSPLASH_ACCESS_KEY', '')
param sqlAdminLogin = readEnvironmentVariable('SQL_ADMIN_LOGIN', 'quakeadmin')
param sqlAdminPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD', '')

// Non-secret config mirrors local.settings.json defaults.
param usgsMinMagnitude = '4.5'
param usgsPollSchedule = '0 */5 * * * *'
