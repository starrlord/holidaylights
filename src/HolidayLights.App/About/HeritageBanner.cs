using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HolidayLights.App.Shell;

namespace HolidayLights.App.About;

/// <summary>
/// The 2003 ABOUT banner (480 x 94 DIP, "Modern Edition 6.0" drawn where 5.4 printed "version 5.4") on its pale-blue
/// panel with an 8 DIP rounded border, with the ABOUTFLASH light strip over the banner's row of bulbs (centred at the top,
/// as 5.4 drew it) alternating every 500 ms (PRODUCT-SPEC 3.9, 4.3.5). With Smooth Fading the strips cross-fade like the
/// lights; under reduced motion only the first strip shows. Used by About and the Welcome card.
/// </summary>
public sealed class HeritageBanner : Border
{
    /// <summary>The banner size in DIP.</summary>
    public static readonly Size BannerSize = new(480, 94);

    /// <summary>The light strip size in DIP.</summary>
    public static readonly Size StripSize = new(296, 15);

    private static readonly TimeSpan FlashInterval = TimeSpan.FromMilliseconds(500);
    private static readonly Duration CrossFade = new(TimeSpan.FromMilliseconds(120));

    private readonly Image secondStrip;
    private readonly DispatcherTimer? timer;
    private readonly bool smoothFading;
    private bool showingSecond;

    /// <summary>Creates the banner.</summary>
    /// <param name="animate">Alternate the strips (false under reduced motion: the first strip stays).</param>
    /// <param name="smoothFading">Cross-fade the strips (the current Look's Smooth Fading).</param>
    public HeritageBanner(bool animate, bool smoothFading)
    {
        this.smoothFading = smoothFading;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(0, 0, 0, 6);
        HorizontalAlignment = HorizontalAlignment.Center;
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, AppResourceKeys.HeritageSkyBrush);
        SetResourceReference(BorderBrushProperty, "CardStrokeColorDefaultBrush");
        AutomationProperties.SetName(this, "Holiday Lights, Modern Edition 6.0");

        var stage = new Grid { Width = BannerSize.Width, Height = BannerSize.Height };
        stage.Children.Add(Picture(AppAssets.AboutBanner, BannerSize));
        stage.Children.Add(Strip(AppAssets.AboutFlash1));
        secondStrip = Strip(AppAssets.AboutFlash2);
        secondStrip.Opacity = 0;
        stage.Children.Add(secondStrip);
        Child = stage;

        if (animate)
        {
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = FlashInterval };
            timer.Tick += (_, _) => Flash();
            Loaded += (_, _) => timer.Start();
            Unloaded += (_, _) => timer.Stop();
        }
    }

    private static Image Strip(string uri)
    {
        Image strip = Picture(uri, StripSize);
        strip.HorizontalAlignment = HorizontalAlignment.Center;
        strip.VerticalAlignment = VerticalAlignment.Top;
        return strip;
    }

    private static Image Picture(string uri, Size size)
    {
        var image = new Image
        {
            Source = new BitmapImage(AppAssetUris.Resolve(uri)),
            Width = size.Width,
            Height = size.Height,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Fant);
        return image;
    }

    private void Flash()
    {
        showingSecond = !showingSecond;
        double target = showingSecond ? 1 : 0;
        if (smoothFading)
        {
            secondStrip.BeginAnimation(OpacityProperty, new DoubleAnimation(target, CrossFade));
        }
        else
        {
            secondStrip.BeginAnimation(OpacityProperty, null);
            secondStrip.Opacity = target;
        }
    }
}
