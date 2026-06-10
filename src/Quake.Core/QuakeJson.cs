using System.Text.Json;

namespace Quake.Core;

/// <summary>
/// The single serializer configuration shared across every boundary — Service Bus
/// messages, blob cards, and the HTTP API. The poller and builder MUST use these
/// same options: a mismatch silently produces null fields rather than errors.
/// </summary>
public static class QuakeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
