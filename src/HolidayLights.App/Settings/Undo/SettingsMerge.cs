using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace HolidayLights.App.Settings.Undo;

/// <summary>
/// Three-way merge of settings snapshots, the basis of Undo and Redo: a recorded step knows the settings before and after
/// it; undoing it puts back exactly the values the step changed and keeps everything else of the current settings
/// (window placement, onboarding flags and other bookkeeping that changed in between).
/// </summary>
/// <remarks>
/// <para>The merge walks the immutable settings records property by property. A nested settings record (any record class
/// of <c>HolidayLights.Core.Abstractions</c>) is merged recursively; everything else is a leaf that is replaced as a whole:
/// values, lists, dictionaries and the arrangement (<see cref="SlotAssignment"/> changes as one unit, like the 8 boxes it
/// describes).</para>
/// <para>Records are copied with their compiler-generated clone method and changed through their setters, so published
/// instances are never modified.</para>
/// </remarks>
public static class SettingsMerge
{
    private static readonly ConcurrentDictionary<Type, RecordShape?> Shapes = new();

    /// <summary>Applies the reverse of a change: every value that differs between <paramref name="target"/> and <paramref name="changed"/> takes <paramref name="target"/>'s value; the rest stays as in <paramref name="current"/>.</summary>
    /// <param name="current">The settings now.</param>
    /// <param name="target">The values to bring back (the settings before the step for Undo; after it for Redo).</param>
    /// <param name="changed">The values the step left (after it for Undo; before it for Redo).</param>
    /// <returns>The merged settings, or <paramref name="current"/> itself when nothing changes.</returns>
    public static AppSettings Apply(AppSettings current, AppSettings target, AppSettings changed)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(changed);
        return (AppSettings)Merge(current, target, changed)!;
    }

    /// <summary>True when two leaf values are equal (sequences element by element, records by value).</summary>
    /// <param name="a">One value.</param>
    /// <param name="b">The other value.</param>
    /// <returns>True when equal.</returns>
    public static bool ValuesEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b) || Equals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a is string || b is string)
        {
            return false;
        }

        if (a is IEnumerable left && b is IEnumerable right)
        {
            return left.Cast<object?>().SequenceEqual(right.Cast<object?>(), ElementComparer.Instance);
        }

        return false;
    }

    private static object? Merge(object? current, object? target, object? changed)
    {
        if (ValuesEqual(target, changed))
        {
            return current;
        }

        if (current is null || target is null || changed is null
            || current.GetType() != target.GetType() || changed.GetType() != target.GetType()
            || ShapeOf(target.GetType()) is not { } shape)
        {
            return target;
        }

        object? result = null;
        foreach (PropertyInfo property in shape.Properties)
        {
            object? currentValue = property.GetValue(current);
            object? merged = Merge(currentValue, property.GetValue(target), property.GetValue(changed));
            if (ReferenceEquals(merged, currentValue))
            {
                continue;
            }

            result ??= shape.CloneMethod.Invoke(current, null);
            property.SetValue(result, merged);
        }

        return result ?? current;
    }

    private static RecordShape? ShapeOf(Type type) => Shapes.GetOrAdd(type, static t =>
    {
        bool isSettingsRecord = t.IsClass
            && t != typeof(SlotAssignment)
            && t.Namespace == typeof(AppSettings).Namespace
            && t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null;
        if (!isSettingsRecord)
        {
            return null;
        }

        PropertyInfo[] properties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToArray();
        return new RecordShape(t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance)!, properties);
    });

    private sealed record RecordShape(MethodInfo CloneMethod, PropertyInfo[] Properties);

    /// <summary>Compares sequence elements (nested sequences such as dictionary values element by element).</summary>
    private sealed class ElementComparer : IEqualityComparer<object?>
    {
        public static ElementComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) =>
            x is not null && IsKeyValuePair(x.GetType()) ? KeyValueEquals(x, y) : ValuesEqual(x, y);

        public int GetHashCode(object? obj) => obj?.GetHashCode() ?? 0;

        private static bool IsKeyValuePair(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);

        private static bool KeyValueEquals(object? x, object? y)
        {
            if (x is null || y is null || x.GetType() != y.GetType())
            {
                return false;
            }

            PropertyInfo key = x.GetType().GetProperty("Key")!;
            PropertyInfo value = x.GetType().GetProperty("Value")!;
            return ValuesEqual(key.GetValue(x), key.GetValue(y)) && ValuesEqual(value.GetValue(x), value.GetValue(y));
        }
    }
}
