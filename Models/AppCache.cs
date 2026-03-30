namespace FileStitcher.Models;

public class AppCache
{
    public string RootFolder { get; set; } = string.Empty;
    public List<string> SelectedFiles { get; set; } = [];
}

public class SelectedFileItem
{
    public string FullPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Icon { get; set; } = "≡";
}
