namespace HolidayLights.App.ScreenSaver.Simulation;

/// <summary>Where the message may move (5.4 <c>Saver_Start_Continue</c>), in DIPs.</summary>
/// <param name="Left">Left of the text box (W / 8).</param>
/// <param name="Right">Right of the text box (W - W / 8); lines are centred between <paramref name="Left"/> and this.</param>
/// <param name="MinTop">The highest top of the text.</param>
/// <param name="MaxBottom">The lowest bottom of the text.</param>
internal readonly record struct TextRange(int Left, int Right, int MinTop, int MaxBottom)
{
    /// <summary>Width of the text box.</summary>
    public int Width => Right - Left;
}

/// <summary>
/// The bouncing message (5.4 <c>Saver_DrawMessage</c>): it starts at the top of its range, moves 1 DIP per step and turns
/// around one step after crossing either end; a message taller than its range stays still.
/// </summary>
internal sealed class TextBounce
{
    private int bottom;

    /// <summary>Starts the message at the top of its range.</summary>
    /// <param name="range">The range.</param>
    /// <param name="textHeight">The height of the wrapped text in DIPs.</param>
    public TextBounce(TextRange range, int textHeight)
    {
        Range = range;
        TextHeight = textHeight;
        Top = range.MinTop;
        PreviousTop = Top;
        bottom = Top + textHeight;
        Velocity = bottom <= range.MaxBottom ? 1 : 0;
    }

    /// <summary>The range.</summary>
    public TextRange Range { get; }

    /// <summary>The height of the wrapped text.</summary>
    public int TextHeight { get; }

    /// <summary>The top of the text now.</summary>
    public int Top { get; private set; }

    /// <summary>The top before the last step.</summary>
    public int PreviousTop { get; private set; }

    /// <summary>+1 (down), -1 (up) or 0 (too tall to move).</summary>
    public int Velocity { get; private set; }

    /// <summary>One step: reverse when outside the range, then move.</summary>
    public void Step()
    {
        PreviousTop = Top;
        if (Top < Range.MinTop || bottom > Range.MaxBottom)
        {
            Velocity = -Velocity;
        }

        Top += Velocity;
        bottom += Velocity;
    }
}
