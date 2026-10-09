using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace HolidayLights.App.ScreenSaver.Rendering;

/// <summary>
/// The installed fonts as 5.4 saw them (PRODUCT-SPEC 6.2.3, 3.5.4; review r1 #37). 5.4 checked the GDI faces
/// (<c>EnumFontFamiliesEx</c>) at saver start; WPF files several of those faces under another family (Arial Black is
/// Arial's Black face, Footlight MT Light is Footlight MT's Light face), so a check of WPF family names alone treats them
/// as missing. Here a font is installed when WPF lists its family or resolves the name to an installed face, and the
/// list for the font picker adds every GDI face WPF can draw.
/// </summary>
public static unsafe partial class InstalledFonts
{
    private const byte DefaultCharSet = 1;

    private static readonly Lazy<HashSet<string>> WpfNames = new(ReadWpfNames, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<IReadOnlyList<string>> Names = new(ReadAll, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly ConcurrentDictionary<string, bool> Resolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every installed family (its English name) and every GDI face WPF can draw, A-Z, without duplicates.</summary>
    public static IReadOnlyList<string> All => Names.Value;

    /// <summary>True when a font is installed: WPF lists the family (any language's name) or resolves the name to an installed face.</summary>
    /// <param name="family">The family or GDI face name (case-insensitive).</param>
    /// <returns>True when installed.</returns>
    public static bool IsInstalled(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return false;
        }

        return WpfNames.Value.Contains(family) || Resolved.GetOrAdd(family, Resolves);
    }

    /// <summary>True when WPF resolves the name to an installed face (not to its fallback font).</summary>
    private static bool Resolves(string family)
    {
        try
        {
            var typeface = new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            return typeface.TryGetGlyphTypeface(out _);
        }
        catch (Exception e) when (e is ArgumentException or UriFormatException or System.IO.IOException or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }

    private static HashSet<string> ReadWpfNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FontFamily family in Fonts.SystemFontFamilies)
        {
            names.Add(family.Source);
            foreach (string name in family.FamilyNames.Values)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static List<string> ReadAll()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FontFamily family in Fonts.SystemFontFamilies)
        {
            string name = family.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-us"), out string? english) ? english : family.Source;
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        foreach (string face in GdiFaces())
        {
            if (!names.Contains(face) && !WpfNames.Value.Contains(face) && IsInstalled(face))
            {
                names.Add(face);
            }
        }

        return [.. names.Order(StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The GDI face names (<c>EnumFontFamiliesExW</c>, every character set), vertical <c>@</c> faces left out.</summary>
    private static HashSet<string> GdiFaces()
    {
        var faces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        GCHandle handle = GCHandle.Alloc(faces);
        nint dc = CreateCompatibleDC(0);
        try
        {
            if (dc != 0)
            {
                var logFont = default(LogFont);
                logFont.CharSet = DefaultCharSet;
                _ = EnumFontFamiliesEx(dc, &logFont, &OnFace, GCHandle.ToIntPtr(handle), 0);
            }
        }
        finally
        {
            if (dc != 0)
            {
                _ = DeleteDC(dc);
            }

            handle.Free();
        }

        return faces;
    }

    [UnmanagedCallersOnly]
    private static int OnFace(LogFont* logFont, nint metrics, uint fontType, nint state)
    {
        if (GCHandle.FromIntPtr(state).Target is HashSet<string> faces)
        {
            string face = new(logFont->FaceName);
            if (face.Length > 0 && face[0] != '@')
            {
                faces.Add(face);
            }
        }

        return 1;
    }

    [LibraryImport("gdi32.dll", EntryPoint = "EnumFontFamiliesExW")]
    private static partial int EnumFontFamiliesEx(nint dc, LogFont* logFont, delegate* unmanaged<LogFont*, nint, uint, nint, int> callback, nint state, uint flags);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    /// <summary>LOGFONTW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LogFont
    {
        public int Height;
        public int Width;
        public int Escapement;
        public int Orientation;
        public int Weight;
        public byte Italic;
        public byte Underline;
        public byte StrikeOut;
        public byte CharSet;
        public byte OutPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;
        public fixed char FaceName[32];
    }
}
