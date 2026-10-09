using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using HolidayLights.App.ScreenSaver.Rendering;

namespace HolidayLights.App.Controls;

/// <summary>One item of the <see cref="FontPicker"/>.</summary>
/// <param name="Family">The family name stored in the settings.</param>
/// <param name="Text">The whole text ("Creepy (not installed - using Chiller)" for a missing theme font): type-to-search and tooltips.</param>
/// <param name="Face">The face the family name is drawn in.</param>
/// <param name="Note">"(not installed - using Chiller)" for a missing theme font, drawn in the UI font; null otherwise.</param>
public sealed record FontChoice(string Family, string Text, FontFamily Face, string? Note = null)
{
    /// <summary>The part drawn in <see cref="Face"/>: the family name.</summary>
    public string Name => Note is null ? Text : Family;

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// The font picker of the screen saver message (PRODUCT-SPEC 3.0.1, 3.5.4): every installed family A-Z, each name drawn in
/// its own face, virtualized, type-to-search. A family that is not installed is listed first as "Creepy (not installed -
/// using Chiller)" while it is chosen: the name in the substitute's face, the note in the UI font (decorative faces make
/// small text hard to read), trimmed with an ellipsis when the box is narrow and complete in the tooltip.
/// </summary>
public class FontPicker : ComboBox
{
    /// <summary>Identifies <see cref="Family"/>.</summary>
    public static readonly DependencyProperty FamilyProperty =
        DependencyProperty.Register(nameof(Family), typeof(string), typeof(FontPicker),
            new FrameworkPropertyMetadata("Arial", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((FontPicker)d).ShowFamily()));

    private bool showing;

    /// <summary>Creates the picker with the Fluent combo box look.</summary>
    public FontPicker()
    {
        SetResourceReference(StyleProperty, typeof(ComboBox));
        IsTextSearchEnabled = true;
        TextSearch.SetTextPath(this, nameof(FontChoice.Text));
        VirtualizingPanel.SetIsVirtualizing(this, true);
        VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
        ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        ItemTemplate = new DataTemplate(typeof(FontChoice)) { VisualTree = ItemVisual() };
        ShowFamily();
    }

    /// <summary>The chosen family.</summary>
    public string Family
    {
        get => (string)GetValue(FamilyProperty);
        set => SetValue(FamilyProperty, value);
    }

    /// <summary>
    /// True when a font is installed (case-insensitive), including GDI faces that WPF files under another family, such as
    /// Arial Black (review r1 #37; the saver's own rule, <see cref="InstalledFonts"/>).
    /// </summary>
    /// <param name="family">The family.</param>
    /// <returns>True when installed.</returns>
    public static bool IsInstalled(string family) => InstalledFonts.IsInstalled(family);

    /// <summary>The item for a family: its name in its own face, or for a missing family the name in the substitute's face and the note.</summary>
    /// <param name="family">The family.</param>
    /// <param name="isInstalled">Tells whether a family is installed.</param>
    /// <returns>The item.</returns>
    public static FontChoice ChoiceFor(string family, Func<string, bool> isInstalled)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(isInstalled);
        if (isInstalled(family))
        {
            return new FontChoice(family, family, new FontFamily(family));
        }

        SaverFont resolved = SaverFontSubstitutes.Resolve(new SaverFont { Family = family }, isInstalled);
        string note = $"(not installed - using {resolved.Family})";
        return new FontChoice(family, $"{family} {note}", new FontFamily(resolved.Family), note);
    }

    /// <inheritdoc />
    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (!showing && SelectedItem is FontChoice choice && !string.Equals(choice.Family, Family, StringComparison.Ordinal))
        {
            Family = choice.Family;
        }

        if (SelectedItem is FontChoice { Note: not null } missing)
        {
            ToolTip = missing.Text;
        }
        else
        {
            ClearValue(ToolTipProperty);
        }
    }

    /// <summary>The name in its face, then the note (if any) in the UI font, trimmed with an ellipsis when the box is narrow.</summary>
    private static FrameworkElementFactory ItemVisual()
    {
        var panel = new FrameworkElementFactory(typeof(DockPanel));
        panel.SetValue(DockPanel.LastChildFillProperty, true);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(FontChoice.Name)));
        name.SetBinding(TextBlock.FontFamilyProperty, new Binding(nameof(FontChoice.Face)));
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        name.SetValue(DockPanel.DockProperty, Dock.Left);
        var note = new FrameworkElementFactory(typeof(TextBlock));
        note.SetBinding(TextBlock.TextProperty, new Binding(nameof(FontChoice.Note)));
        note.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        note.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        note.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 0, 0));
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.AppendChild(name);
        panel.AppendChild(note);
        return panel;
    }

    private void ShowFamily()
    {
        showing = true;
        try
        {
            string family = Family;
            var items = new List<FontChoice>();
            IReadOnlyList<string> installed = InstalledFonts.All;
            if (!installed.Contains(family, StringComparer.OrdinalIgnoreCase))
            {
                // A missing family gets its "(not installed - using X)" item; an installed face the list does not name
                // (for example "Arial Bold") is listed as it is, so the choice always shows.
                items.Add(ChoiceFor(family, IsInstalled));
            }

            items.AddRange(installed.Select(name => new FontChoice(name, name, new FontFamily(name))));
            ItemsSource = items;
            SelectedItem = items.FirstOrDefault(i => string.Equals(i.Family, family, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            showing = false;
        }
    }
}
