using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Quake.Core.Models;

namespace Quake.Core.Services;

/// <summary>
/// Builds an Atom 1.0 (RFC 4287) feed of recent story cards. Pure and Azure-free so
/// it is unit-testable. Uses <see cref="XDocument"/>, which escapes all text and
/// attribute content for us — never hand-concatenate XML here.
/// </summary>
public static class AtomFeedBuilder
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    /// <summary>The feed's own stable id, independent of any entry.</summary>
    public const string FeedId = "tag:earthquake-story-machine,2026:feed";

    /// <param name="items">Newest-first list of cards to publish (caller applies the cap).</param>
    /// <param name="selfUrl">Absolute URL the feed is served from (the &lt;link rel="self"&gt;).</param>
    /// <returns>A serialized Atom 1.0 XML document (UTF-8, with declaration).</returns>
    public static string Build(IReadOnlyList<StoryFeedItem> items, string selfUrl)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrEmpty(selfUrl);

        // Feed-level updated = newest entry's time; empty feed falls back to "now".
        var updated = items.Count > 0
            ? items.Max(i => i.OccurredUtc)
            : DateTimeOffset.UtcNow;

        var feed = new XElement(Atom + "feed",
            new XElement(Atom + "title", "Earthquake Story Machine"),
            new XElement(Atom + "subtitle", "Recent earthquakes, told as story cards"),
            new XElement(Atom + "id", FeedId),
            new XElement(Atom + "updated", Rfc3339(updated)),
            new XElement(Atom + "link", new XAttribute("rel", "self"), new XAttribute("href", selfUrl)),
            new XElement(Atom + "generator", "Earthquake Story Machine"));

        foreach (var item in items)
            feed.Add(BuildEntry(item));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), feed);
        return Serialize(doc);
    }

    private static XElement BuildEntry(StoryFeedItem item)
    {
        var where = LocationLabel(item);
        var title = $"M{item.Magnitude.ToString("0.0", CultureInfo.InvariantCulture)} — {where}";
        var summary =
            $"A magnitude {item.Magnitude.ToString("0.0", CultureInfo.InvariantCulture)} earthquake " +
            $"occurred at {item.Place} on {item.OccurredUtc.UtcDateTime:yyyy-MM-dd HH:mm} UTC.";

        return new XElement(Atom + "entry",
            new XElement(Atom + "title", title),
            new XElement(Atom + "id", EntryId(item.QuakeId)),
            new XElement(Atom + "updated", Rfc3339(item.OccurredUtc)),
            new XElement(Atom + "published", Rfc3339(item.OccurredUtc)),
            new XElement(Atom + "summary", summary));
    }

    /// <summary>Stable, opaque entry id as a tag URI derived from the USGS quake id.</summary>
    public static string EntryId(string quakeId) => $"tag:earthquake-story-machine,2026:quake:{quakeId}";

    private static string LocationLabel(StoryFeedItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.City) && !string.IsNullOrWhiteSpace(item.Country))
            return $"{item.City}, {item.Country}";
        if (!string.IsNullOrWhiteSpace(item.Country))
            return item.Country!;
        return item.Place;
    }

    private static string Rfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Serialize(XDocument doc)
    {
        using var sw = new Utf8StringWriter();
        var settings = new XmlWriterSettings { Indent = true, Encoding = sw.Encoding };
        using (var xw = XmlWriter.Create(sw, settings))
            doc.Save(xw);
        return sw.ToString();
    }

    /// <summary>StringWriter that reports UTF-8 so the XML declaration reads encoding="utf-8".</summary>
    private sealed class Utf8StringWriter : StringWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    }
}
