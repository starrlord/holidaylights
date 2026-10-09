using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace HolidayLights.App.Gallery;

/// <summary>One bulb of the Bulb List: a tile or a Details row (PRODUCT-SPEC 3.2.5) and the selected-bulb bar (3.2.6).</summary>
public sealed class BulbItem : INotifyPropertyChanged
{
    /// <summary>How long a bulb shows the "New" pill after it was added.</summary>
    public static readonly TimeSpan NewFor = TimeSpan.FromHours(24);

    private bool isFavorite;
    private bool isInUse;

    /// <summary>Creates an item.</summary>
    /// <param name="info">The bulb.</param>
    /// <param name="isFavorite">True for a favorite.</param>
    /// <param name="isInUse">True when the bulb is on the screen.</param>
    /// <param name="isRemoved">True in "Removed Bulbs".</param>
    /// <param name="now">The time, for the "New" pill.</param>
    public BulbItem(BulbInfo info, bool isFavorite, bool isInUse, bool isRemoved, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(info);
        Info = info;
        this.isFavorite = isFavorite;
        this.isInUse = isInUse;
        IsRemoved = isRemoved;
        IsNew = info.AddedDate is { } added && now - added < NewFor && now >= added;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The bulb.</summary>
    public BulbInfo Info { get; }

    /// <summary>Always false: placeholders are <see cref="SkeletonItem"/>s (the item containers bind to it).</summary>
    public bool IsPlaceholder => false;

    /// <summary>The bulb id.</summary>
    public string Id => Info.Id;

    /// <summary>The bulb's name.</summary>
    public string Name => Info.Name;

    /// <summary>The description, verbatim.</summary>
    public string Description => Info.Description;

    /// <summary>"Built-In", "Add-On" or "My Bulb".</summary>
    public string SourceText => SourceOf(Info.Origin);

    /// <summary>The first line of the author field.</summary>
    public string AuthorFirstLine => FirstLine(Info.Author);

    /// <summary>The right-aligned caption of a Details row: "Add-On - Joe Lachoff".</summary>
    public string DetailsCaption => AuthorFirstLine.Length == 0 ? SourceText : $"{SourceText} - {AuthorFirstLine}";

    /// <summary>The categories, comma separated.</summary>
    public string CategoriesText => string.Join(", ", Info.Categories);

    /// <summary>True for a favorite (the filled star).</summary>
    public bool IsFavorite
    {
        get => isFavorite;
        set => Set(ref isFavorite, value);
    }

    /// <summary>True when the bulb is in the arrangement (the accent dot).</summary>
    public bool IsInUse
    {
        get => isInUse;
        set => Set(ref isInUse, value);
    }

    /// <summary>True for 24 hours after the bulb was added (the "New" pill).</summary>
    public bool IsNew { get; }

    /// <summary>True when any cell is larger than 64 px (the "Big" tag).</summary>
    public bool IsBig => Info.IsBig;

    /// <summary>True when an animation of the bulb could not be decoded (the warning glyph).</summary>
    public bool IsDamaged => Info.HasDamagedArt;

    /// <summary>True in "Removed Bulbs" (the tile shows "Restore").</summary>
    public bool IsRemoved { get; }

    /// <summary>"5 colors" (top-edge flavors) or "1 color".</summary>
    public string ColorsText => Info.TopFlavorCount == 1 ? "1 color" : string.Create(CultureInfo.CurrentCulture, $"{Info.TopFlavorCount} colors");

    /// <summary>"flashes" (light bulbs, 5.4), "animated" or "still".</summary>
    public string KindText => Info.TopKind switch
    {
        BulbAnimationKind.LightBulb => "flashes",
        BulbAnimationKind.Animation => "animated",
        _ => "still",
    };

    /// <summary>The facts line of the selected-bulb bar: "5 colors - flashes - 32 x 32 px".</summary>
    public string FactsText => string.Create(CultureInfo.CurrentCulture,
        $"{ColorsText} - {KindText} - {Info.LargestTopCell.Width} x {Info.LargestTopCell.Height} px");

    /// <summary>The credits line: "Art: Joe Lachoff - Copyright 2003 ...".</summary>
    public string CreditsText
    {
        get
        {
            string copyright = FirstLine(Info.Copyright);
            return (AuthorFirstLine.Length, copyright.Length) switch
            {
                (0, 0) => "",
                (0, _) => "Art: " + copyright,
                (_, 0) => "Art: " + AuthorFirstLine,
                _ => $"Art: {AuthorFirstLine} - {copyright}",
            };
        }
    }

    /// <summary>The screen-reader name: "Candy Canes, built-in bulb, 4 colors, animated, favorite, in use".</summary>
    public string AccessibleName
    {
        get
        {
            string source = Info.Origin switch
            {
                BulbOrigin.BuiltIn => "built-in bulb",
                BulbOrigin.BundledAddOn => "add-on bulb",
                _ => "my bulb",
            };
            string text = $"{Name}, {source}, {ColorsText}, {KindText}";
            if (IsFavorite)
            {
                text += ", favorite";
            }

            if (IsInUse)
            {
                text += ", in use";
            }

            if (IsDamaged)
            {
                text += ", damaged";
            }

            return text;
        }
    }

    /// <summary>"Built-In", "Add-On" or "My Bulb".</summary>
    /// <param name="origin">Where the bulb comes from.</param>
    /// <returns>The tag.</returns>
    public static string SourceOf(BulbOrigin origin) => origin switch
    {
        BulbOrigin.BuiltIn => "Built-In",
        BulbOrigin.BundledAddOn => "Add-On",
        _ => "My Bulb",
    };

    /// <inheritdoc />
    public override string ToString() => Name;

    private static string FirstLine(string text)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        return (end < 0 ? text : text[..end]).Trim();
    }

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
    }
}
