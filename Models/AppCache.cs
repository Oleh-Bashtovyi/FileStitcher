namespace FileStitcher.Models;

public class Preset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string RootFolder { get; set; } = string.Empty;
    public List<string> SelectedFiles { get; set; } = [];
}

public class AppCache
{
    public List<Preset> Presets { get; set; } = [];
    public string? ActivePresetId { get; set; }
}

public class SelectedFileItem
{
    public string FullPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Icon { get; set; } = "≡";
}