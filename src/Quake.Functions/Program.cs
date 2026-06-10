using System.Net.Http.Headers;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quake.Core.Abstractions;
using Quake.Core.Services;
using Quake.Data;
using Quake.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((ctx, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        var cfg = ctx.Configuration;

        services.AddHttpClient<IGeocodingClient, NominatimClient>(c =>
        {
            c.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
            // Required by Nominatim usage policy — requests without it get blocked.
            c.DefaultRequestHeaders.UserAgent.ParseAdd("EarthquakeStoryMachine/1.0 (portfolio project)");
        });
        services.AddHttpClient<IWikiClient, WikipediaClient>(c =>
            c.BaseAddress = new Uri("https://en.wikipedia.org/"));
        services.AddHttpClient<IWeatherClient, OpenMeteoClient>(c =>
            c.BaseAddress = new Uri("https://api.open-meteo.com/"));
        services.AddHttpClient<IPhotoClient, UnsplashClient>(c =>
        {
            c.BaseAddress = new Uri("https://api.unsplash.com/");
            // Key is a placeholder until the user supplies one; only attach the header
            // when present so the host still boots (photo enrichment just returns empty).
            var unsplashKey = cfg["UnsplashAccessKey"];
            if (!string.IsNullOrWhiteSpace(unsplashKey) && !unsplashKey.StartsWith('<'))
                c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Client-ID", unsplashKey);
        });
        services.AddHttpClient<IQuakeHistoryClient, UsgsHistoryClient>(c =>
            c.BaseAddress = new Uri("https://earthquake.usgs.gov/"));
        services.AddHttpClient("usgs-feed", c =>
            c.BaseAddress = new Uri("https://earthquake.usgs.gov/"));

        services.AddSingleton(new BlobServiceClient(cfg["BlobStorageConnection"]));
        services.AddSingleton<IStoryCardStore, BlobStoryCardStore>();
        services.AddScoped<StoryCardAssembler>();
        services.AddDbContext<QuakeDbContext>(o => o.UseSqlServer(cfg["SqlConnection"]));
    })
    .Build();

host.Run();
