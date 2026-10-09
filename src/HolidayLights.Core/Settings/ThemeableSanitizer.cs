namespace HolidayLights.Core.Settings;

/// <summary>Repairs the 13 theme values of the current settings and of Recent Settings entries.</summary>
internal static class ThemeableSanitizer
{
    /// <summary>Repairs nulls and out-of-range values.</summary>
    /// <param name="values">The stored values, possibly null when read from a file.</param>
    /// <returns>The same instance when valid, else a repaired copy.</returns>
    public static ThemeableSettings Sanitize(ThemeableSettings? values)
    {
        if (values is null)
        {
            return new ThemeableSettings();
        }

        SlotAssignment arrangement = values.Arrangement is null ? SlotAssignment.Classic54Default : ValueRules.CleanArrangement(values.Arrangement);
        FlashSettings flash = Sanitize(values.Flash);
        CurrentMusic music = Sanitize(values.Music);
        SaverLook saver = Sanitize(values.Saver);
        bool unchanged = ReferenceEquals(arrangement, values.Arrangement) && ReferenceEquals(flash, values.Flash)
            && ReferenceEquals(music, values.Music) && ReferenceEquals(saver, values.Saver);
        return unchanged ? values : new ThemeableSettings { Arrangement = arrangement, Flash = flash, Music = music, Saver = saver };
    }

    private static FlashSettings Sanitize(FlashSettings? flash)
    {
        if (flash is null)
        {
            return new FlashSettings();
        }

        FlashPatternId pattern = Enum.IsDefined(flash.Pattern) ? flash.Pattern : FlashPatternId.FlashTogether;
        int interval = ValueRules.NormalizeInterval(flash.Interval);
        return pattern == flash.Pattern && interval == flash.Interval ? flash : new FlashSettings { Pattern = pattern, Interval = interval };
    }

    private static CurrentMusic Sanitize(CurrentMusic? music)
    {
        if (music is null)
        {
            return new CurrentMusic();
        }

        IReadOnlyList<string> disabled = ValueRules.CleanIdSet(music.DisabledSongs, ValueRules.IsMediaId, MediaIds.Comparer);
        PlayMode mode = Enum.IsDefined(music.Mode) ? music.Mode : PlayMode.Always;
        return ReferenceEquals(disabled, music.DisabledSongs) && mode == music.Mode
            ? music
            : new CurrentMusic { DisabledSongs = disabled, Mode = mode };
    }

    private static SaverLook Sanitize(SaverLook? saver)
    {
        if (saver is null)
        {
            return new SaverLook();
        }

        string animation = ValueRules.CanonicalAnimation(saver.Animation) ?? SaverAnimations.Snow;
        SaverMovementStyle style = Enum.IsDefined(saver.Style) ? saver.Style : SaverAnimations.DefaultStyleFor(animation);
        string message = ValueRules.CleanMessage(saver.Message);
        SaverFont font = ValueRules.CleanFont(saver.Font);
        string picture = SanitizePicture(saver.Picture);
        PicturePlacement placement = Enum.IsDefined(saver.Placement) ? saver.Placement : PicturePlacement.Center;
        bool unchanged = animation == saver.Animation && style == saver.Style && ReferenceEquals(message, saver.Message)
            && ReferenceEquals(font, saver.Font) && picture == saver.Picture && placement == saver.Placement;
        return unchanged
            ? saver
            : saver with { Animation = animation, Style = style, Message = message, Font = font, Picture = picture, Placement = placement };
    }

    /// <summary>A missing picture is the 5.4 default; a malformed one cannot be shown, so it becomes "(None)".</summary>
    private static string SanitizePicture(string? picture) =>
        string.IsNullOrWhiteSpace(picture) ? SaverPictures.Default
        : ValueRules.IsValidPicture(picture) ? picture
        : SaverPictures.None;
}
