using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace HolidayLights.App.Settings.Dialogs;

/// <summary>
/// The Import Details dialog (PRODUCT-SPEC 3.8.5): caption "Imported from Holiday Lights 5.4", a read-only list of every
/// 5.4 item with "Imported", "Already included" or "Not imported - &lt;reason&gt;", and the date. [Copy] [Close].
/// </summary>
public sealed class ImportDetailsDialog : DialogWindow
{
    private readonly Import54Record record;

    /// <summary>Creates the dialog.</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="record">The import record.</param>
    public ImportDetailsDialog(Window? owner, Import54Record record)
        : base(owner, "Imported from Holiday Lights 5.4")
    {
        ArgumentNullException.ThrowIfNull(record);
        this.record = record;
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.CanResize;
        Width = 640;
        Height = 560;
        var list = new ListView { Focusable = true };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        System.Windows.Automation.AutomationProperties.SetName(list, "Imported items");
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Item", Width = 310, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Item)) });
        view.Columns.Add(new GridViewColumn { Header = "Result", Width = 240, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Result)) });
        list.View = view;
        list.ItemsSource = record.Report.Select(i => new Row(i.Item, StatusText(i))).ToArray();
        var date = new TextBlock { Text = "Imported on " + record.Date.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture) + ".", Margin = new Thickness(0, 0, 0, 10) };
        var root = new DockPanel();
        DockPanel.SetDock(date, Dock.Top);
        root.Children.Add(date);
        root.Children.Add(list);
        Body = root;
        AddButton("_Copy", isPrimary: false, isCancel: false, Copy);
        AddButton("Close", isPrimary: true, isCancel: true, Close);
    }

    /// <summary>Shows the dialog over the Settings window.</summary>
    /// <param name="host">The Settings window.</param>
    /// <param name="record">The import record.</param>
    public static void Show(ISettingsHost host, Import54Record record)
    {
        ArgumentNullException.ThrowIfNull(host);
        new ImportDetailsDialog(host.Window, record).ShowDialog();
    }

    /// <summary>"Imported", "Already included" or "Not imported - &lt;reason&gt;".</summary>
    /// <param name="item">The report item.</param>
    /// <returns>The text.</returns>
    public static string StatusText(ImportReportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Status switch
        {
            ImportItemStatus.Imported => "Imported",
            ImportItemStatus.AlreadyIncluded => "Already included",
            _ => string.IsNullOrWhiteSpace(item.Reason) ? "Not imported" : "Not imported - " + item.Reason,
        };
    }

    /// <summary>"Copy": the report as text (one item per line) on the clipboard.</summary>
    private void Copy()
    {
        var text = new StringBuilder();
        text.AppendLine("Imported from Holiday Lights 5.4 on " + record.Date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture));
        foreach (ImportReportItem item in record.Report)
        {
            text.Append(item.Item).Append('\t').AppendLine(StatusText(item));
        }

        try
        {
            Clipboard.SetText(text.ToString());
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The clipboard is busy (another app holds it); the copy is skipped like Windows' own Copy commands do.
        }
    }

    /// <summary>One row of the list.</summary>
    /// <param name="Item">The 5.4 item.</param>
    /// <param name="Result">What happened.</param>
    private sealed record Row(string Item, string Result);
}
