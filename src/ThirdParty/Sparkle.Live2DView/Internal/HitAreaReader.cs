using System.Text.Json;

namespace Sparkle.Live2DView.Internal;

internal static class HitAreaReader
{
    public static IReadOnlyList<Live2DHitArea> Read(string modelJsonPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(modelJsonPath));
        if (!document.RootElement.TryGetProperty("HitAreas", out JsonElement hitAreas))
            return [];

        var result = new List<Live2DHitArea>();
        foreach (JsonElement item in hitAreas.EnumerateArray())
        {
            if (!item.TryGetProperty("Name", out JsonElement nameElement) ||
                !item.TryGetProperty("Id", out JsonElement idElement))
                continue;

            string? name = nameElement.GetString();
            string? id = idElement.GetString();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id))
                result.Add(new Live2DHitArea(name, id));
        }

        return result;
    }
}
