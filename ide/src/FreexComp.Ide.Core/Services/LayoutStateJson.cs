using System.Text.Json;
using System.Text.Json.Nodes;
using FreexComp.Ide.Core.Models;

namespace FreexComp.Ide.Core.Services;

/// <summary>
/// The <c>window</c>, <c>activeProfile</c> and <c>profiles</c> sections of <c>uistate.json</c> (architecture.md §8.1,
/// IDE-ARCH-060/061). Reading validates per section (IDE-ARCH-063): a section that does not have the expected shape
/// yields null and its default is used; tool names outside <paramref name="knownTools"/> are ignored, a name listed twice
/// keeps its first occurrence, built-in members missing from the file return to their built-in group, and dock sizes
/// below the minimums of UI-WIN-005 are raised to them (UI-WIN-054).
/// </summary>
internal static class LayoutStateJson
{
    private const double SideMin = 160;
    private const double BottomMin = 80;

    public static WindowPlacement? ReadWindow(JsonNode? node)
    {
        if (node is not JsonObject window
            || Int(window["x"]) is not { } x || Int(window["y"]) is not { } y
            || Int(window["width"]) is not { } width || Int(window["height"]) is not { } height
            || Str(window["state"]) is not { } state || state is not ("normal" or "maximized"))
        {
            return null;
        }

        return new WindowPlacement(x, y, width, height, state == "maximized");
    }

    public static JsonObject WriteWindow(WindowPlacement placement) => new()
    {
        ["x"] = placement.X,
        ["y"] = placement.Y,
        ["width"] = placement.Width,
        ["height"] = placement.Height,
        ["state"] = placement.Maximized ? "maximized" : "normal",
    };

    /// <summary>A profile as stored, merged with its built-in definition; null when its shape is invalid.</summary>
    public static ProfileLayout? ReadProfile(string name, JsonNode? node, IReadOnlySet<string> knownTools)
    {
        if (node is not JsonObject profile
            || profile["left"] is not JsonObject left || profile["right"] is not JsonObject right
            || profile["bottom"] is not JsonObject bottom
            || right["upper"] is not JsonObject upper || right["lower"] is not JsonObject lower
            || Num(left["size"]) is not { } leftSize || Num(right["size"]) is not { } rightSize
            || Num(bottom["size"]) is not { } bottomSize || Num(right["split"]) is not { } split)
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var groups = new Dictionary<ToolGroupId, ToolGroupLayout>();
        foreach (var (id, json) in new[] { (ToolGroupId.Left, left), (ToolGroupId.RightUpper, upper), (ToolGroupId.RightLower, lower), (ToolGroupId.Bottom, bottom) })
        {
            if (ReadGroup(json, knownTools, seen) is not { } group)
            {
                return null;
            }

            groups[id] = group;
        }

        var result = new ProfileLayout
        {
            LeftSize = Math.Max(SideMin, leftSize),
            RightSize = Math.Max(SideMin, rightSize),
            BottomSize = Math.Max(BottomMin, bottomSize),
            RightSplit = Math.Clamp(split, 0, 1),
            Left = groups[ToolGroupId.Left],
            RightUpper = groups[ToolGroupId.RightUpper],
            RightLower = groups[ToolGroupId.RightLower],
            Bottom = groups[ToolGroupId.Bottom],
        };

        // Tool windows the file does not mention go where the built-in profile puts them.
        var builtIn = LayoutProfiles.Create(name);
        foreach (var id in Enum.GetValues<ToolGroupId>())
        {
            var from = builtIn.Group(id);
            var to = result.Group(id);
            to.Tools.AddRange(from.Tools.Where(seen.Add));
            to.Hidden.AddRange(from.Hidden.Where(seen.Add));
        }

        return result;
    }

    public static JsonObject WriteProfile(ProfileLayout profile) => new()
    {
        ["left"] = WriteGroup(profile.Left, profile.LeftSize),
        ["right"] = new JsonObject
        {
            ["size"] = profile.RightSize,
            ["split"] = profile.RightSplit,
            ["upper"] = WriteGroup(profile.RightUpper, null),
            ["lower"] = WriteGroup(profile.RightLower, null),
        },
        ["bottom"] = WriteGroup(profile.Bottom, profile.BottomSize),
    };

    public static double? Num(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : null;

    private static ToolGroupLayout? ReadGroup(JsonObject json, IReadOnlySet<string> knownTools, HashSet<string> seen)
    {
        if (Names(json["tools"]) is not { } tools || Names(json["hidden"]) is not { } hidden)
        {
            return null;
        }

        var active = json["active"] is null ? null : Str(json["active"]);
        if (json["active"] is not null && active is null)
        {
            return null;
        }

        bool Keep(string name) => knownTools.Contains(name) && seen.Add(name);
        return new ToolGroupLayout
        {
            Tools = [.. tools.Where(Keep)],
            Hidden = [.. hidden.Where(Keep)],
            Active = active is not null && knownTools.Contains(active) ? active : null,
        };
    }

    private static JsonObject WriteGroup(ToolGroupLayout group, double? size)
    {
        var json = new JsonObject();
        if (size is { } s)
        {
            json["size"] = s;
        }

        json["tools"] = new JsonArray([.. group.Tools.Select(t => (JsonNode)t)]);
        json["hidden"] = new JsonArray([.. group.Hidden.Select(t => (JsonNode)t)]);
        json["active"] = group.Active;
        return json;
    }

    private static List<string>? Names(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return null;
        }

        var names = new List<string>();
        foreach (var item in array)
        {
            if (Str(item) is not { } name)
            {
                return null;
            }

            names.Add(name);
        }

        return names;
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    private static int? Int(JsonNode? node) =>
        Num(node) is { } d && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue ? (int)d : null;
}
