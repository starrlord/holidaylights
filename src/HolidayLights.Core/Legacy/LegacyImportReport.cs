using System.Globalization;

namespace HolidayLights.Core.Legacy;

/// <summary>
/// The lines of "Import Details" (PRODUCT-SPEC 6.8.5, 3.8.5): every 5.4 item as "Imported", "Already included" or "Not
/// imported - &lt;reason&gt;", in the order main key, category overrides, themes, files, startup shortcut.
/// </summary>
internal sealed class LegacyImportReport
{
    /// <summary>The lines so far.</summary>
    public List<ImportReportItem> Items { get; } = [];

    /// <summary>Adds one line.</summary>
    /// <param name="item">The 5.4 item.</param>
    /// <param name="status">What happened.</param>
    /// <param name="reason">Why it was not imported.</param>
    public void Add(string item, ImportItemStatus status, string? reason = null) =>
        Items.Add(new ImportReportItem { Item = item, Status = status, Reason = reason });

    /// <summary>One line per value of the main key, in the order of the 5.4 table, unknown values last.</summary>
    /// <param name="main">The main key.</param>
    /// <param name="issues">Problems of the current values.</param>
    /// <param name="hotKeyImported">False when the hot key letter could not be used.</param>
    public void AddMainValues(LegacyValueSet main, LegacyMappingIssues issues, bool hotKeyImported)
    {
        var order = LegacyValueNames.ReportOrder.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index);
        foreach (string name in main.Names.Select(LegacyValueNames.Canonical)
            .OrderBy(n => order.GetValueOrDefault(n, int.MaxValue)).ThenBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            (ImportItemStatus status, string? reason) = name switch
            {
                _ when LegacyValueNames.Obsolete.Contains(name) => (ImportItemStatus.NotImported, "obsolete"),
                LegacyValueNames.EnabledMusic => (ImportItemStatus.AlreadyIncluded, (string?)null),
                LegacyValueNames.SaverModule when issues.UnknownAnimation => (ImportItemStatus.NotImported, "unknown animation; Snow is used"),
                LegacyValueNames.HotKeyChar when !hotKeyImported => (ImportItemStatus.NotImported, "a hot key can't use this key"),
                _ when order.ContainsKey(name) => (ImportItemStatus.Imported, null),
                _ => (ImportItemStatus.NotImported, "unknown setting"),
            };
            Add(name, status, reason);
        }

        AddUnresolvedBulbs(LegacyValueNames.BulbSettings, issues);
    }

    /// <summary>One line per bulb id that resolved to no installed bulb.</summary>
    /// <param name="item">The item the ids belong to ("Bulb Settings", "Themes\Grandma's Lights").</param>
    /// <param name="issues">The problems.</param>
    public void AddUnresolvedBulbs(string item, LegacyMappingIssues issues)
    {
        foreach (int id in issues.UnresolvedBulbs.Order())
        {
            Add($"{item} (bulb id {id.ToString(CultureInfo.InvariantCulture)})", ImportItemStatus.NotImported, "the bulb isn't installed");
        }
    }
}
