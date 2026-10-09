using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.App.BulbFactory;

/// <summary>
/// Shows the unsaved Bulb Editing document in a <c>LightStrip</c> (the edge sample, CONTRACTS 11.9): the document's bulb id
/// resolves to a transient bulb made from it; every other id resolves through the catalog.
/// </summary>
/// <remarks>Immutable: each revision of the document gets a new resolver, so the strip notices the change.</remarks>
internal sealed class DocumentBulbResolver : IBulbResolver
{
    private readonly IBulbResolver fallback;
    private readonly string bulbId;
    private readonly IBulb? bulb;

    private DocumentBulbResolver(IBulbResolver fallback, string bulbId, IBulb? bulb)
    {
        this.fallback = fallback;
        this.bulbId = bulbId;
        this.bulb = bulb;
    }

    /// <summary>
    /// Makes the transient bulb of a document revision (any thread): the document is written to memory as a bulb file
    /// and read back like any add-on, with a content key of its own so cached art is never mixed up.
    /// </summary>
    /// <param name="fallback">The catalog.</param>
    /// <param name="bulbId">The edited bulb's id.</param>
    /// <param name="document">A snapshot of the document.</param>
    /// <param name="contentKey">A key unique to this revision.</param>
    /// <returns>
    /// The resolver; the bulb is missing (skipped on screen) while the document lacks a required animation or is larger
    /// than a bulb file can be.
    /// </returns>
    public static DocumentBulbResolver Create(IBulbResolver fallback, string bulbId, BulbDocument document, string contentKey)
    {
        if (!document.IsComplete)
        {
            return new DocumentBulbResolver(fallback, bulbId, null);
        }

        BulbDocument named = document.Clone();
        if (string.IsNullOrWhiteSpace(named.Name))
        {
            named.Name = BulbIds.GetKey(bulbId);
        }

        byte[] bytes;
        try
        {
            bytes = BulFileWriter.Encode(named, 0).Bytes;
        }
        catch (InvalidOperationException)
        {
            return new DocumentBulbResolver(fallback, bulbId, null);
        }

        return new DocumentBulbResolver(fallback, bulbId, AddOnBulbs.FromFile(BulFile.Parse(bytes), bulbId, BulbOrigin.UserAddOn, contentKey));
    }

    /// <summary>A content key for one revision of one editing session.</summary>
    /// <param name="session">The session's unique id.</param>
    /// <param name="revision">The revision.</param>
    /// <returns>The key.</returns>
    public static string ContentKeyFor(Guid session, int revision) =>
        string.Create(CultureInfo.InvariantCulture, $"bulb-editing:{session:N}:{revision}");

    /// <inheritdoc />
    public bool TryGetBulb(string id, [NotNullWhen(true)] out IBulb? found)
    {
        if (BulbIds.Comparer.Equals(id, bulbId))
        {
            found = bulb;
            return found is not null;
        }

        return fallback.TryGetBulb(id, out found);
    }
}
