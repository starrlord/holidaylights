namespace HolidayLights.App.BulbFactory;

/// <summary>
/// The texts of Bulb Editing, Edit Categories, New Category, Bulb Credits and Export Bulb File that code builds
/// (PRODUCT-SPEC 3.3, 3.2.10; the 5.4 texts verbatim). Labels that never
/// change live in the windows' XAML.
/// </summary>
internal static class BulbFactoryStrings
{
    /// <summary>Caption of Bulb Editing ("Bulb Editing - Star").</summary>
    public const string EditingCaption = "Bulb Editing";

    /// <summary>Caption of Edit Categories ("Edit Categories - Star").</summary>
    public const string EditCategoriesCaption = "Edit Categories";

    /// <summary>Shown when the name is empty.</summary>
    public const string NameRequired = "Give your bulb a name.";

    /// <summary>Shown while a text holds characters a bulb file cannot store.</summary>
    public const string CharactersWillBeReplaced = "Some characters can't be saved in a bulb file and will be replaced by \"?\".";

    /// <summary>Shown while the checked categories do not fit in a bulb file.</summary>
    public const string TooManyCategories = "Too many categories are checked to fit in a bulb file. Uncheck some to save.";

    /// <summary>Text 206 for the bulb list preview.</summary>
    public const string PreviewDescription = "This preview appears in the bulb list. Drag the white square to select the visible portion.";

    /// <summary>Text 206 for a corner.</summary>
    public const string CornerDescription = "This animation appears in the corner of the screen. It has only one flavor.";

    /// <summary>Text 206 for a side.</summary>
    public const string SideDescription = "This animation appears along the edge of the screen. It can have more than one flavor.";

    /// <summary>Text 205 for the bulb list preview.</summary>
    public const string PreviewHint = "Press Change to choose a new animation for the bulb list preview.";

    /// <summary>Text 205 for a corner.</summary>
    public const string CornerHint = "Press Change to choose a new animation for the corner of the screen.";

    /// <summary>Text 205 for flavor 1 of a side.</summary>
    public const string FirstFlavorHint = "Press Change to choose a new animation.\r\n(The first flavor cannot be removed.)";

    /// <summary>Text 205 for flavors 2-8 that have an animation.</summary>
    public const string RemovableFlavorHint = "Press Change to choose a new animation.\r\nPress Remove Flavor to delete this flavor.";

    /// <summary>Text 205 for flavors 2-8 without an animation.</summary>
    public const string EmptyFlavorHint = "Press Change to choose a new animation.";

    /// <summary>The preview of a flavor without an animation.</summary>
    public const string NoAnimation =
        "No animation has been chosen for this flavor.\r\n\r\nA \"flavor\" is an additional picture for the same bulb. For "
        + "example, the Holiday Lights \"Snow Family\" has four flavors, each one showing a different snow figure. All four "
        + "flavors are seen simultaneously when the bulb is used.\r\n\r\nYou can ignore flavors entirely if you wish to create "
        + "only simple bulbs.";

    /// <summary>The preview of an animation that cannot be decoded.</summary>
    public const string DamagedAnimation = "This animation is damaged and can't be shown. Press Change to choose a new animation.";

    /// <summary>The GIF tip (PRODUCT-SPEC 3.3.1).</summary>
    public const string GifTip =
        "This GIF only stores what changes between frames. Holiday Lights draws GIFs exactly like version 5.4, so some parts "
        + "may look see-through. Saving the GIF with full frames avoids this.";

    /// <summary>"Frame 1 of 4".</summary>
    public const string FrameFormat = "Frame {0} of {1}";

    /// <summary>"2x".</summary>
    public const string ZoomFormat = "{0}x";

    /// <summary>The flavor combo items ("Flavor 1", "Flavor 2 *").</summary>
    public const string FlavorFormat = "Flavor {0}";

    /// <summary>Appended to a flavor that has an animation.</summary>
    public const string FlavorInUseMark = " *";

    /// <summary>Title of a GIF that cannot be used (5.4).</summary>
    public const string CannotImportGif = "Cannot Import GIF File";

    /// <summary>Why a GIF cannot be used for a slot (5.4).</summary>
    public const string GifProblem = "A problem occurred when importing the GIF file. It cannot be used with this bulb.";

    /// <summary>A GIF that could not become a bulb because a file could not be written ({0} = Windows reason).</summary>
    public const string GifNotWrittenFormat = "Sorry, this GIF file can't be imported. {0}";

    /// <summary>Title of a picture that cannot be used.</summary>
    public const string CannotImportPicture = "Cannot Import Picture";

    /// <summary>Why a PNG picture cannot be used.</summary>
    public const string PictureProblem = "A problem occurred when importing the picture. It cannot be used with this bulb.";

    /// <summary>PNG frames of different sizes.</summary>
    public const string PicturesDifferInSize = "The pictures must all be the same size to become the frames of one animation.";

    /// <summary>A picture too big for a bulb ({0} = the largest size).</summary>
    public const string PictureTooBigFormat = "Pictures for bulbs can be at most {0:N0} x {0:N0} pixels.";

    /// <summary>A GIF file too big to be a bulb animation ({0} = megabytes).</summary>
    public const string GifTooBigFormat = "This GIF is too big for a bulb. Bulb animations can be at most {0} MB.";

    /// <summary>Several GIFs, or GIFs and PNGs, were chosen together.</summary>
    public const string ChooseOneAnimation = "Choose one GIF file, or one or more PNG pictures to use as the frames of one animation.";

    /// <summary>A file that is neither a GIF nor a PNG.</summary>
    public const string UnsupportedPicture = "Holiday Lights can use GIF files (.gif) and PNG pictures (.png) for bulbs.";

    /// <summary>Title of an error message about a chosen file.</summary>
    public const string ProblemWithFile = "Problem Importing File";

    /// <summary>"Cannot Import GIF File: A problem occurred ..." (title and text in one InfoBar sentence).</summary>
    public const string ErrorMessageFormat = "{0}: {1}";

    /// <summary>Saving failed ({0} = file name, {1} = Windows reason).</summary>
    public const string SaveFailedFormat = "Couldn't save \"{0}\": {1}";

    /// <summary>A required slot has no animation ({0} = slot name).</summary>
    public const string MissingAnimationFormat = "Choose an animation for the {0} before saving.";

    /// <summary>After Copy to All Sides ({0} = side name).</summary>
    public const string CopiedToAllSidesFormat = "The other sides now use the flavors of the {0}. Press Ctrl+Z to undo.";

    /// <summary>"Discard Changes?" confirmation title.</summary>
    public const string DiscardTitle = "Discard Changes?";

    /// <summary>"Your changes to Star will be lost." ({0} = bulb name).</summary>
    public const string DiscardTextFormat = "Your changes to {0} will be lost.";

    /// <summary>The destructive button of the confirmation.</summary>
    public const string Discard = "Discard";

    /// <summary>The safe button of the confirmation.</summary>
    public const string KeepEditing = "Keep Editing";

    /// <summary>Filter of "Change..." (5.4 "GIF Files", plus PNG frames).</summary>
    public const string PictureFilter = "GIF Files (*.gif)|*.gif|PNG Pictures (*.png)|*.png|GIF and PNG Pictures (*.gif;*.png)|*.gif;*.png";

    /// <summary>Title of the "Change..." file dialog.</summary>
    public const string ChangeDialogTitle = "Change Animation";

    /// <summary>Filter of "Export Bulb File...".</summary>
    public const string BulbFilter = "Bulb Files (*.bul)|*.bul";

    /// <summary>Title of the "Export Bulb File..." dialog.</summary>
    public const string ExportDialogTitle = "Export Bulb File";

    /// <summary>Title of a failed export.</summary>
    public const string ExportFailedTitle = "Couldn't Export Bulb File";

    /// <summary>A failed export ({0} = file name, {1} = Windows reason; copy deck "Couldn't copy ...").</summary>
    public const string CopyFailedFormat = "Couldn't copy \"{0}\": {1}";

    /// <summary>The retry button of a failed export.</summary>
    public const string TryAgain = "Try Again";

    /// <summary>The cancel button of a failed export.</summary>
    public const string Cancel = "Cancel";

    /// <summary>The source caption of a built-in bulb in Bulb Credits.</summary>
    public const string BuiltInSource = "Built-in bulb from Holiday Lights 5.4";

    /// <summary>The source caption of a bundled add-on bulb ({0} = file name).</summary>
    public const string AddOnSourceFormat = "Add-on bulb included with Holiday Lights - {0}";

    /// <summary>The source caption of a My Bulbs bulb ({0} = file path).</summary>
    public const string MyBulbSourceFormat = "My Bulb - {0}";

    /// <summary>The 5.4 notice of Bulb Credits.</summary>
    public const string ArtNotice = "This art may not be used for other purposes without the author's permission.";

    /// <summary>The Undo text of a Bulb Editing save ({0} = bulb name).</summary>
    public const string EditUndoFormat = "Edit {0}";
}
