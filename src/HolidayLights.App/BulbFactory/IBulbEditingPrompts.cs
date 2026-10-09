namespace HolidayLights.App.BulbFactory;

/// <summary>Asks for a new category name (the "New Category" dialog).</summary>
internal interface INewCategoryPrompt
{
    /// <summary>Shows "New Category".</summary>
    /// <returns>The typed name, or null when cancelled.</returns>
    string? AskNewCategory();
}

/// <summary>The questions Bulb Editing asks through its window (a seam for tests).</summary>
internal interface IBulbEditingPrompts : INewCategoryPrompt
{
    /// <summary>"Change...": the open-file dialog for one GIF or several PNG frames.</summary>
    /// <returns>The chosen files, or null when cancelled.</returns>
    IReadOnlyList<string>? PickPictureFiles();

    /// <summary>"Discard Changes?" / "Your changes to &lt;name&gt; will be lost." [Discard] [Keep Editing].</summary>
    /// <param name="bulbName">The bulb as it was opened.</param>
    /// <returns>True for Discard.</returns>
    Task<bool> ConfirmDiscardAsync(string bulbName);
}
