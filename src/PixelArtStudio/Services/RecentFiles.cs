using System.IO;
using System.Text.Json;

namespace PixelArtStudio.Services;

/// <summary>Most-recently-used file list stored in %AppData%\PixelArtStudio\recent.json.</summary>
public static class RecentFiles
{
    public const int MaxCount = 10;

    private static readonly string StorePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelArtStudio", "recent.json");

    public static List<string> Load()
    {
        try
        {
            return File.Exists(StorePath)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StorePath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<string> paths)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(paths.Take(MaxCount).ToList()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth interrupting the user over.
        }
    }
}
