using System.Text.Json;
using Azure.Storage.Blobs;
using Quake.Core;
using Quake.Core.Abstractions;
using Quake.Core.Models;

namespace Quake.Functions.Services;

public sealed class BlobStoryCardStore(BlobServiceClient blobService) : IStoryCardStore
{
    private const string Container = "story-cards";

    // Clone the shared pipeline serializer so blob cards round-trip identically to
    // Service Bus and the API; only WriteIndented differs (readable stored JSON).
    private static readonly JsonSerializerOptions Json =
        new(QuakeJson.Options) { WriteIndented = true };

    public async Task<string> SaveAsync(StoryCard card, CancellationToken ct = default)
    {
        var container = blobService.GetBlobContainerClient(Container);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        var path = $"{card.Quake.OccurredUtc:yyyy/MM}/{card.Quake.Id}.json";
        await container.GetBlobClient(path)
            .UploadAsync(BinaryData.FromString(JsonSerializer.Serialize(card, Json)), overwrite: true, ct);
        return path;
    }

    public async Task<StoryCard?> GetAsync(string quakeId, CancellationToken ct = default)
    {
        var container = blobService.GetBlobContainerClient(Container);
        await foreach (var blob in container.GetBlobsAsync(prefix: null, cancellationToken: ct))
        {
            if (!blob.Name.EndsWith($"/{quakeId}.json", StringComparison.Ordinal)) continue;
            var content = await container.GetBlobClient(blob.Name).DownloadContentAsync(ct);
            return JsonSerializer.Deserialize<StoryCard>(content.Value.Content.ToString(), Json);
        }
        return null;
    }
}
