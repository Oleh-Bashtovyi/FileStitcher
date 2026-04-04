namespace FileStitcher.Models;

public enum SeparatorType { None, EmptyLine, Header, Custom }

public class Preset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string RootFolder { get; set; } = string.Empty;
    public List<string> SelectedFiles { get; set; } = [];
    public List<string> Extensions { get; set; } = [".cs", ".txt", ".json"];
    public SeparatorType SeparatorType { get; set; } = SeparatorType.Header;
    public int EmptyLineCount { get; set; } = 1;
    public string CustomSeparatorTemplate { get; set; } =
        "//========================\n{{RelativePath}}:\n//========================";
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