using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HolidayLights.App.Controls;

namespace HolidayLights.App.Settings;

/// <summary>
/// The "Peek" header button of Home and Bulb Factory (PRODUCT-SPEC 3.2.3, View glyph): press and hold to see the desktop
/// while held; a short click, Enter or Space peeks for 3 s.
/// </summary>
public class PeekButton : Button
{
    /// <summary>Creates the button ("_Peek" with the View glyph and its tooltip; the Fluent button style).</summary>
    public PeekButton()
    {
        SetResourceReference(StyleProperty, typeof(Button));
        Content = "_Peek";
        ButtonContent.SetGlyph(this, Glyphs.View);
        SetResourceReference(ContentTemplateProperty, "HL.Template.GlyphLabel");
        SetResourceReference(ToolTipProperty, "HL.Tip.BulbFactory.Peek");
        System.Windows.Automation.AutomationProperties.SetName(this, "Peek");
    }

    private Peek? Peek => Window.GetWindow(this) is ISettingsHost host ? host.Peek : null;

    /// <inheritdoc />
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Peek is { } peek)
        {
            Focus();
            CaptureMouse();
            peek.Press();
            e.Handled = true;
        }

        base.OnPreviewMouseLeftButtonDown(e);
    }

    /// <inheritdoc />
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            Peek?.Release();
            e.Handled = true;
        }

        base.OnPreviewMouseLeftButtonUp(e);
    }

    /// <inheritdoc />
    protected override void OnClick()
    {
        base.OnClick();
        Peek?.PeekTimed();
    }
}
