using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace HolidayLights.App.Controls;

/// <summary>
/// The tile hover of PRODUCT-SPEC 4.4: a hovered tile lifts (scale 1.03 and a soft shadow) over 120 ms, and settles back
/// when the pointer leaves; nothing moves while Windows animation effects are off. Set on item containers, so a recycled
/// container always starts settled.
/// </summary>
public static class HoverLift
{
    /// <summary>The lifted scale.</summary>
    public const double LiftedScale = 1.03;

    /// <summary>Identifies the attached <c>IsEnabled</c> property.</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(HoverLift), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly Duration Duration = new(TimeSpan.FromMilliseconds(120));

    /// <summary>Whether the element lifts while hovered.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The value.</returns>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>Sets whether the element lifts while hovered.</summary>
    /// <param name="element">The element.</param>
    /// <param name="value">The value.</param>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.MouseEnter -= OnMouseEnter;
        element.MouseLeave -= OnMouseLeave;
        element.DataContextChanged -= OnDataContextChanged;
        if ((bool)e.NewValue)
        {
            element.MouseEnter += OnMouseEnter;
            element.MouseLeave += OnMouseLeave;
            element.DataContextChanged += OnDataContextChanged;
        }
        else
        {
            Settle(element, animate: false);
        }
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && SystemParameters.ClientAreaAnimation)
        {
            Lift(element);
        }
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            Settle(element, animate: SystemParameters.ClientAreaAnimation);
        }
    }

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            Settle(element, animate: false);
        }
    }

    private static void Lift(FrameworkElement element)
    {
        if (element.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
        {
            scale = new ScaleTransform(1, 1);
            element.RenderTransform = scale;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        if (element.Effect is not DropShadowEffect shadow)
        {
            shadow = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Direction = 270, Opacity = 0, Color = Colors.Black };
            element.Effect = shadow;
        }

        Panel.SetZIndex(element, 1);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(LiftedScale, Duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(LiftedScale, Duration) { EasingFunction = ease });
        shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.28, Duration) { EasingFunction = ease });
    }

    private static void Settle(FrameworkElement element, bool animate)
    {
        Panel.SetZIndex(element, 0);
        if (element.RenderTransform is ScaleTransform { IsFrozen: false } scale)
        {
            if (animate)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, Duration));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, Duration));
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }
        }

        if (element.Effect is DropShadowEffect shadow)
        {
            if (animate)
            {
                var fade = new DoubleAnimation(0, Duration);
                fade.Completed += (_, _) =>
                {
                    if (!element.IsMouseOver && ReferenceEquals(element.Effect, shadow))
                    {
                        element.Effect = null;
                    }
                };
                shadow.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
            }
            else
            {
                element.Effect = null;
            }
        }
    }
}
