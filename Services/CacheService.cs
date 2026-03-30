using System.IO;
using System.Text.Json;
using FileStitcher.Models;

namespace FileStitcher.Services;

public class CacheService
{
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileStitcher",
        "cache.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppCache? Load()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var json = File.ReadAllText(CachePath);
            return JsonSerializer.Deserialize<AppCache>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save(AppCache cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(cache, JsonOptions));
        }
        catch
        {
            // Silently ignore — cache is non-critical
        }
    }
}
