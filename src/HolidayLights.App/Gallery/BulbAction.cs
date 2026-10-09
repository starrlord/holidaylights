namespace HolidayLights.App.Gallery;

/// <summary>A command on bulbs of the Bulb List or the selected-bulb bar that the page carries out (PRODUCT-SPEC 3.2.6, 3.2.11).</summary>
public enum BulbAction
{
    /// <summary>"Use for &lt;Target&gt;" (double-click, Enter).</summary>
    Use,

    /// <summary>"Add to Top Edge" ... (appends; <see cref="BulbActionRequest.Side"/> names the edge).</summary>
    AddToEdge,

    /// <summary>"Add to Every Edge" (Shift+Enter).</summary>
    AddToEveryEdge,

    /// <summary>"Use in All Corners".</summary>
    UseInAllCorners,

    /// <summary>"Use Everywhere".</summary>
    UseEverywhere,

    /// <summary>"Use for the Top-Left Corner" ... from the Add To flyout (<see cref="BulbActionRequest.Corner"/>).</summary>
    UseInCorner,

    /// <summary>"Edit Bulb..." or "Edit Categories...".</summary>
    Edit,

    /// <summary>"Bulb Credits".</summary>
    Credits,

    /// <summary>"Export Bulb File...".</summary>
    Export,

    /// <summary>"Show in Folder".</summary>
    ShowInFolder,

    /// <summary>"Use as Screen Saver Animation".</summary>
    UseAsSaverAnimation,

    /// <summary>"Remove Bulb".</summary>
    Remove,
}

/// <summary>A <see cref="BulbAction"/> on bulbs.</summary>
/// <param name="Action">The command.</param>
/// <param name="BulbIds">The bulbs, in selection order.</param>
/// <param name="Side">The edge of <see cref="BulbAction.AddToEdge"/>.</param>
/// <param name="Corner">The corner of <see cref="BulbAction.UseInCorner"/>.</param>
public sealed record BulbActionRequest(BulbAction Action, IReadOnlyList<string> BulbIds, Side Side = Side.Top, Corner Corner = Corner.TopLeft);
