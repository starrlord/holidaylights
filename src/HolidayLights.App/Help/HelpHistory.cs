namespace HolidayLights.App.Help;

/// <summary>The Back and Forward history of the Help window (Alt+Left, Alt+Right). Not thread-safe (UI thread).</summary>
public sealed class HelpHistory
{
    private readonly List<string> topics = [];
    private int position = -1;

    /// <summary>The topic shown, or null before the first navigation.</summary>
    public string? Current => position >= 0 ? topics[position] : null;

    /// <summary>True when there is a topic to go back to.</summary>
    public bool CanGoBack => position > 0;

    /// <summary>True when there is a topic to go forward to.</summary>
    public bool CanGoForward => position >= 0 && position < topics.Count - 1;

    /// <summary>Records a new topic; the forward history is discarded. Navigating to the current topic changes nothing.</summary>
    /// <param name="topicId">The topic.</param>
    public void Navigate(string topicId)
    {
        ArgumentException.ThrowIfNullOrEmpty(topicId);
        if (string.Equals(Current, topicId, StringComparison.Ordinal))
        {
            return;
        }

        topics.RemoveRange(position + 1, topics.Count - position - 1);
        topics.Add(topicId);
        position = topics.Count - 1;
    }

    /// <summary>Goes back one topic.</summary>
    /// <returns>The topic to show, or null when there is none.</returns>
    public string? Back()
    {
        if (!CanGoBack)
        {
            return null;
        }

        position--;
        return Current;
    }

    /// <summary>Goes forward one topic.</summary>
    /// <returns>The topic to show, or null when there is none.</returns>
    public string? Forward()
    {
        if (!CanGoForward)
        {
            return null;
        }

        position++;
        return Current;
    }
}
