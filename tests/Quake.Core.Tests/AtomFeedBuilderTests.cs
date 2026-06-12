using System.Xml.Linq;
using Quake.Core.Models;
using Quake.Core.Services;
using Xunit;

namespace Quake.Core.Tests;

public class AtomFeedBuilderTests
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private const string SelfUrl = "http://localhost:7071/api/feed";

    private static StoryFeedItem Item(
        string id, double mag, string place,
        string? city = null, string? country = null, DateTimeOffset occurred = default) =>
        new()
        {
            QuakeId = id,
            Magnitude = mag,
            Place = place,
            City = city,
            Country = country,
            OccurredUtc = occurred == default ? new DateTimeOffset(2026, 6, 10, 8, 30, 0, TimeSpan.Zero) : occurred,
        };

    [Fact]
    public void Builds_wellformed_atom_with_one_entry_per_item()
    {
        var items = new[]
        {
            Item("us7000a", 5.1, "7 km NW of Nuing, Philippines", "Nuing", "Philippines"),
            Item("us7000b", 4.8, "south of the Fiji Islands", country: "Fiji"),
        };

        var xml = AtomFeedBuilder.Build(items, SelfUrl);
        var doc = XDocument.Parse(xml); // throws if not well-formed

        Assert.Equal(Atom + "feed", doc.Root!.Name);
        Assert.Equal(2, doc.Root.Elements(Atom + "entry").Count());
        Assert.Equal(AtomFeedBuilder.FeedId, doc.Root.Element(Atom + "id")!.Value);

        var self = doc.Root.Elements(Atom + "link").Single(e => (string?)e.Attribute("rel") == "self");
        Assert.Equal(SelfUrl, (string?)self.Attribute("href"));
    }

    [Fact]
    public void Entry_id_is_stable_tag_uri_derived_from_quake_id()
    {
        var xml = AtomFeedBuilder.Build([Item("us7000xyz", 5.0, "somewhere")], SelfUrl);
        var doc = XDocument.Parse(xml);

        var entryId = doc.Root!.Element(Atom + "entry")!.Element(Atom + "id")!.Value;
        Assert.Equal("tag:earthquake-story-machine,2026:quake:us7000xyz", entryId);
        Assert.Equal(entryId, AtomFeedBuilder.EntryId("us7000xyz"));
    }

    [Fact]
    public void Escapes_xml_special_characters_in_card_text()
    {
        // Ampersand and angle brackets in the place must be escaped, not break the XML.
        var place = "M&S building <strike> & \"quotes\" at 5 < 6";
        var xml = AtomFeedBuilder.Build([Item("usAmp", 6.0, place)], SelfUrl);

        // Raw markup must NOT leak into the serialized output as live tags.
        Assert.DoesNotContain("<strike>", xml);
        Assert.Contains("&amp;", xml);
        Assert.Contains("&lt;strike&gt;", xml);

        // And it must round-trip back to the exact original text via a real XML parse.
        var doc = XDocument.Parse(xml);
        var summary = doc.Root!.Element(Atom + "entry")!.Element(Atom + "summary")!.Value;
        Assert.Contains(place, summary);
    }

    [Fact]
    public void Empty_feed_is_valid_and_has_no_entries()
    {
        var xml = AtomFeedBuilder.Build([], SelfUrl);
        var doc = XDocument.Parse(xml); // must still be well-formed

        Assert.Empty(doc.Root!.Elements(Atom + "entry"));
        // feed-level updated still present (falls back to now) — required by RFC 4287.
        Assert.NotNull(doc.Root.Element(Atom + "updated"));
        Assert.False(string.IsNullOrWhiteSpace(doc.Root.Element(Atom + "updated")!.Value));
    }

    [Fact]
    public void Feed_updated_equals_newest_entry_time()
    {
        var older = new DateTimeOffset(2026, 6, 8, 1, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 6, 11, 23, 15, 0, TimeSpan.Zero);

        // Pass in non-sorted order to prove the builder uses Max, not first/last.
        var items = new[]
        {
            Item("old", 5.0, "older place", occurred: older),
            Item("new", 5.5, "newer place", occurred: newer),
        };

        var xml = AtomFeedBuilder.Build(items, SelfUrl);
        var doc = XDocument.Parse(xml);

        Assert.Equal("2026-06-11T23:15:00Z", doc.Root!.Element(Atom + "updated")!.Value);
    }

    [Fact]
    public void Entries_preserve_caller_order()
    {
        var items = new[]
        {
            Item("first", 5.0, "a", occurred: new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero)),
            Item("second", 5.0, "b", occurred: new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero)),
            Item("third", 5.0, "c", occurred: new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero)),
        };

        var xml = AtomFeedBuilder.Build(items, SelfUrl);
        var doc = XDocument.Parse(xml);

        var ids = doc.Root!.Elements(Atom + "entry")
            .Select(e => e.Element(Atom + "id")!.Value)
            .ToArray();

        Assert.Equal(
            ["tag:earthquake-story-machine,2026:quake:first",
             "tag:earthquake-story-machine,2026:quake:second",
             "tag:earthquake-story-machine,2026:quake:third"],
            ids);
    }

    [Fact]
    public void Dates_are_rfc3339_utc_with_z_suffix()
    {
        var xml = AtomFeedBuilder.Build(
            [Item("z", 5.0, "p", occurred: new DateTimeOffset(2026, 6, 10, 8, 30, 0, TimeSpan.FromHours(5)))],
            SelfUrl);
        var doc = XDocument.Parse(xml);

        // 08:30 +05:00 normalizes to 03:30Z.
        var updated = doc.Root!.Element(Atom + "entry")!.Element(Atom + "updated")!.Value;
        Assert.Equal("2026-06-10T03:30:00Z", updated);
    }
}
