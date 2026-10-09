using System.Globalization;

namespace HolidayLights.App.Install;

/// <summary>What installing does to the installation that is already there (the setup window words its choice by it).</summary>
public enum InstallKind
{
    /// <summary>Nothing is installed yet.</summary>
    New,

    /// <summary>An older version is installed.</summary>
    Update,

    /// <summary>The same version is installed (installing repairs it).</summary>
    Reinstall,

    /// <summary>A newer version is installed.</summary>
    Downgrade,
}

/// <summary>Compares the installed version with the setup's (semantic versions: <c>6.0.2</c>, <c>6.1.0-beta.1</c>).</summary>
public static class InstallKinds
{
    /// <summary>What installing a version does over another.</summary>
    /// <param name="installedVersion">The installed version (<see cref="InstallManifest.Version"/>), or null when nothing is installed.</param>
    /// <param name="setupVersion">The version being installed.</param>
    /// <returns>The kind; an installed version that cannot be read counts as older.</returns>
    public static InstallKind For(string? installedVersion, string setupVersion)
    {
        ArgumentNullException.ThrowIfNull(setupVersion);
        if (installedVersion is null)
        {
            return InstallKind.New;
        }

        return Compare(installedVersion, setupVersion) switch
        {
            < 0 => InstallKind.Update,
            0 => InstallKind.Reinstall,
            _ => InstallKind.Downgrade,
        };
    }

    /// <summary>
    /// Orders two versions as semantic versioning does: the numbers first, then a pre-release (<c>-beta.1</c>) before the
    /// release; build metadata (<c>+abc</c>) is ignored. A version that cannot be read sorts before every other.
    /// </summary>
    /// <param name="left">A version.</param>
    /// <param name="right">Another version.</param>
    /// <returns>Negative, zero or positive.</returns>
    public static int Compare(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        bool leftRead = TryRead(left, out Version? leftNumbers, out string leftLabel);
        bool rightRead = TryRead(right, out Version? rightNumbers, out string rightLabel);
        if (!leftRead || !rightRead)
        {
            return leftRead || rightRead ? leftRead.CompareTo(rightRead) : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        int numbers = leftNumbers!.CompareTo(rightNumbers);
        if (numbers != 0)
        {
            return numbers;
        }

        if (leftLabel.Length == 0 || rightLabel.Length == 0)
        {
            // A release comes after its pre-releases.
            return rightLabel.Length.CompareTo(leftLabel.Length);
        }

        return CompareLabels(leftLabel.Split('.'), rightLabel.Split('.'));
    }

    private static bool TryRead(string version, out Version? numbers, out string label)
    {
        string text = version.Trim();
        int metadata = text.IndexOf('+', StringComparison.Ordinal);
        if (metadata >= 0)
        {
            text = text[..metadata];
        }

        int dash = text.IndexOf('-', StringComparison.Ordinal);
        label = dash >= 0 ? text[(dash + 1)..] : "";
        string core = dash >= 0 ? text[..dash] : text;
        if (!Version.TryParse(core, out numbers))
        {
            return false;
        }

        // 6.0 and 6.0.0 are the same version.
        numbers = new Version(numbers.Major, numbers.Minor, Math.Max(0, numbers.Build), Math.Max(0, numbers.Revision));
        return true;
    }

    /// <summary>Pre-release identifiers one by one: numbers numerically and before words, then the longer list last.</summary>
    private static int CompareLabels(string[] left, string[] right)
    {
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            bool leftNumber = int.TryParse(left[i], NumberStyles.None, CultureInfo.InvariantCulture, out int leftValue);
            bool rightNumber = int.TryParse(right[i], NumberStyles.None, CultureInfo.InvariantCulture, out int rightValue);
            int order = (leftNumber, rightNumber) switch
            {
                (true, true) => leftValue.CompareTo(rightValue),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left[i], right[i]),
            };
            if (order != 0)
            {
                return order;
            }
        }

        return left.Length.CompareTo(right.Length);
    }
}
