using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace HolidayLights.Core.Settings;

/// <summary>
/// Reads settings and theme JSON leniently: a value of the wrong type (a hand edit, an enum value written by a newer
/// version) is removed and takes its default instead of making the whole file unreadable.
/// </summary>
internal static class JsonSalvage
{
    /// <summary>The most values removed from one document before it counts as unreadable.</summary>
    public const int MaxRemovedValues = 32;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parses text into a JSON object.</summary>
    /// <param name="json">The text.</param>
    /// <param name="root">The object when parsing succeeded.</param>
    /// <param name="problem">Why the text is not a JSON object, when parsing failed.</param>
    /// <returns>True for a JSON object.</returns>
    public static bool TryParseObject(string json, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JsonObject? root, out string? problem)
    {
        root = null;
        problem = null;
        try
        {
            if (JsonNode.Parse(json, documentOptions: DocumentOptions) is JsonObject parsed)
            {
                // Touch every property once so that duplicate keys fail here and not later.
                _ = parsed.Count;
                root = parsed;
                return true;
            }

            problem = "the file does not contain a JSON object";
            return false;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            problem = e.Message;
            return false;
        }
    }

    /// <summary>Deserializes <paramref name="root"/>, removing values that cannot be read.</summary>
    /// <typeparam name="T">The contract type.</typeparam>
    /// <param name="root">The document; values that cannot be read are removed from it.</param>
    /// <param name="typeInfo">The source-generated metadata.</param>
    /// <param name="removedPaths">Receives the JSON paths of removed values.</param>
    /// <returns>The value.</returns>
    /// <exception cref="JsonException">The document cannot be read even after removing values.</exception>
    public static T Deserialize<T>(JsonObject root, JsonTypeInfo<T> typeInfo, ICollection<string> removedPaths)
    {
        while (true)
        {
            try
            {
                return root.Deserialize(typeInfo) ?? throw new JsonException("The document is empty.");
            }
            catch (JsonException e)
            {
                if (removedPaths.Count >= MaxRemovedValues || e.Path is not { } path || !TryRemove(root, path))
                {
                    throw;
                }

                removedPaths.Add(path);
            }
        }
    }

    /// <summary>Removes the node at a JSON path such as <c>$.current.arrangement.top[2]</c> or <c>$['a b'].c</c>.</summary>
    /// <param name="root">The document.</param>
    /// <param name="path">The path reported by <see cref="JsonException.Path"/>.</param>
    /// <returns>True when a node was removed.</returns>
    public static bool TryRemove(JsonObject root, string path)
    {
        if (!TryParsePath(path, out List<object>? segments) || segments.Count == 0)
        {
            return false;
        }

        JsonNode? parent = root;
        for (int i = 0; i < segments.Count - 1 && parent is not null; i++)
        {
            parent = Child(parent, segments[i]);
        }

        return (parent, segments[^1]) switch
        {
            (JsonObject obj, string name) => obj.Remove(name),
            (JsonArray array, int index) when index < array.Count => RemoveAt(array, index),
            _ => false,
        };
    }

    private static bool RemoveAt(JsonArray array, int index)
    {
        array.RemoveAt(index);
        return true;
    }

    private static JsonNode? Child(JsonNode node, object segment) => (node, segment) switch
    {
        (JsonObject obj, string name) => obj[name],
        (JsonArray array, int index) when index < array.Count => array[index],
        _ => null,
    };

    private static bool TryParsePath(string path, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out List<object>? segments)
    {
        segments = null;
        if (!path.StartsWith('$'))
        {
            return false;
        }

        var result = new List<object>();
        int i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                int end = i + 1;
                while (end < path.Length && path[end] is not ('.' or '['))
                {
                    end++;
                }

                if (end == i + 1)
                {
                    return false;
                }

                result.Add(path[(i + 1)..end]);
                i = end;
            }
            else if (path[i] == '[' && i + 1 < path.Length && path[i + 1] == '\'')
            {
                int end = path.IndexOf("']", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    return false;
                }

                result.Add(path[(i + 2)..end]);
                i = end + 2;
            }
            else if (path[i] == '[')
            {
                int end = path.IndexOf(']', i + 1);
                if (end < 0 || !int.TryParse(path.AsSpan(i + 1, end - i - 1), out int index) || index < 0)
                {
                    return false;
                }

                result.Add(index);
                i = end + 1;
            }
            else
            {
                return false;
            }
        }

        segments = result;
        return true;
    }
}
