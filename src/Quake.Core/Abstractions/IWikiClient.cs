using Quake.Core.Models;

namespace Quake.Core.Abstractions;

public interface IWikiClient
{
    Task<WikiSummary?> GetSummaryAsync(string title, CancellationToken ct = default);
}
