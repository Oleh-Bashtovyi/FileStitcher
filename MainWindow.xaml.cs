using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FileStitcher.Models;
using FileStitcher.Services;

namespace FileStitcher;

public partial class MainWindow : Window
{
    // ── Supported extensions ────────────────────────────────────────
    private static readonly HashSet<string> SupportedExt =
        new(StringComparer.OrdinalIgnoreCase) { ".cs", ".txt", ".json" };

    // Directories to skip while scanning (common noise)
    private static readonly HashSet<string> SkipDirs =

        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules", ".idea" };

    // ── State ───────────────────────────────────────────────────────
    private readonly CacheService _cache = new();
    private readonly ObservableCollection<FileTreeItem> _tree = [];
    private readonly ObservableCollection<SelectedFileItem> _selected = [];
    private readonly HashSet<string> _selectedPaths =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _rootFolder;
    private bool _bulkOp;    // suppresses redundant saves during bulk ops

    // ────────────────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();
        FileTreeView.ItemsSource = _tree;
        SelectedFilesList.ItemsSource = _selected;
        LoadCachedState();
    }



    // ── Cache load/save ─────────────────────────────────────────────
    private void LoadCachedState()
    {
        var cached = _cache.Load();
        if (cached == null) return;

        // If root is gone → start fresh
        if (!Directory.Exists(cached.RootFolder)) return;

        _rootFolder = cached.RootFolder;
        RefreshRootDisplay();
        BuildTree();

        // Restore only files that still exist
        _bulkOp = true;
        foreach (var path in cached.SelectedFiles.Where(File.Exists))
            ApplyFileCheck(path, check: true);
        _bulkOp = false;

        UpdateFooter();
    }

    private void SaveCache()
    {
        _cache.Save(new AppCache
        {
            RootFolder = _rootFolder ?? "",
            SelectedFiles = [.. _selectedPaths]
        });
    }

    // ── Tree building ────────────────────────────────────────────────
    private void BuildTree()
    {
        if (_rootFolder is null) return;

        _tree.Clear();
        try
        {
            var root = BuildNode(new DirectoryInfo(_rootFolder), parent: null);
            if (root != null) _tree.Add(root);
        }
        catch (UnauthorizedAccessException) { /* skip inaccessible roots */ }

        int total = _tree.Sum(CountFiles);
        TxtTreeStatus.Text = total > 0
            ? $"{total} supported file(s) in scope"
            : "No .cs / .json / .txt files found in this folder";
        BtnRefresh.IsEnabled = true;
    }

    private FileTreeItem? BuildNode(DirectoryInfo dir, FileTreeItem? parent)
    {
        var node = new FileTreeItem
        {
            Name = dir.Name,
            FullPath = dir.FullName,
            IsDirectory = true,
            Parent = parent
        };

        // Subdirectories first
        IEnumerable<DirectoryInfo> subDirs;
        try { subDirs = dir.GetDirectories().Where(d => !SkipDirs.Contains(d.Name)).OrderBy(d => d.Name); }
        catch { subDirs = []; }

        foreach (var sub in subDirs)
        {
            var childNode = BuildNode(sub, node);
            if (childNode != null)
                node.Children.Add(childNode);
        }

        // Supported files
        IEnumerable<FileInfo> files;
        try { files = dir.GetFiles().Where(f => SupportedExt.Contains(f.Extension)).OrderBy(f => f.Name); }
        catch { files = []; }

        foreach (var f in files)
        {
            var fileItem = new FileTreeItem
            {
                Name = f.Name,
                FullPath = f.FullName,
                IsDirectory = false,
                Parent = node
            };
            fileItem.PropertyChanged += OnFileItemPropertyChanged;
            node.Children.Add(fileItem);
        }

        // Omit empty directories from the tree
        return node.Children.Count > 0 ? node : null;
    }

    // ── File check/uncheck handler ───────────────────────────────────
    private void OnFileItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FileTreeItem.IsChecked)) return;
        if (sender is not FileTreeItem { IsDirectory: false } item) return;

        if (item.IsChecked == true)
        {
            if (_selectedPaths.Add(item.FullPath))
                InsertSorted(MakeSelectedItem(item.FullPath));
        }
        else
        {
            if (_selectedPaths.Remove(item.FullPath))
            {
                var existing = _selected.FirstOrDefault(s =>
                    s.FullPath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
                if (existing != null) _selected.Remove(existing);
            }
        }

        if (!_bulkOp)
        {
            UpdateFooter();
            SaveCache();
        }
    }

    // ── Browse / Refresh ─────────────────────────────────────────────
    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select Root Folder" };
        if (dlg.ShowDialog() != true) return;

        _rootFolder = dlg.FolderName;
        RefreshRootDisplay();

        _bulkOp = true;
        _tree.Clear();
        _selected.Clear();
        _selectedPaths.Clear();
        _bulkOp = false;

        BuildTree();
        UpdateFooter();
        SaveCache();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_rootFolder is null || !Directory.Exists(_rootFolder))
        {
            SetStatus("⚠ Root folder no longer exists. Please select a new one.");
            _rootFolder = null;
            RefreshRootDisplay();
            _bulkOp = true;
            _tree.Clear(); _selected.Clear(); _selectedPaths.Clear();
            _bulkOp = false;
            UpdateFooter();
            return;
        }

        // Save current selection, rebuild tree, restore
        var savedPaths = _selectedPaths.ToList();

        _bulkOp = true;
        _tree.Clear();
        _selected.Clear();
        _selectedPaths.Clear();

        BuildTree();

        foreach (var path in savedPaths.Where(File.Exists))
            ApplyFileCheck(path, check: true);
        _bulkOp = false;

        UpdateFooter();
        SaveCache();
        SetStatus("Tree refreshed.");
    }

    // ── Select / Clear all ───────────────────────────────────────────
    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        _bulkOp = true;
        foreach (var item in _tree) item.SetIsChecked(true, updateChildren: true, updateParent: false);
        _bulkOp = false;
        UpdateFooter();
        SaveCache();
    }

    private void BtnClearAll_Click(object sender, RoutedEventArgs e)
    {
        _bulkOp = true;
        foreach (var item in _tree) item.SetIsChecked(false, updateChildren: true, updateParent: false);
        _bulkOp = false;
        UpdateFooter();
        SaveCache();
    }

    private void BtnRemoveAll_Click(object sender, RoutedEventArgs e) =>
        BtnClearAll_Click(sender, e);

    // ── Remove single file ───────────────────────────────────────────
    private void BtnRemoveFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            FindAndSetCheck(_tree, path, check: false);
        // PropertyChanged handler does the rest
    }

    // ── CheckBox click: treat indeterminate as "go to checked" ───────
    // With IsThreeState="False", clicking a null checkbox sets it to true — that
    // is handled automatically by the TwoWay binding. This handler exists only
    // to make sure the cascade fires cleanly when clicking indeterminate.
    private void TreeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: FileTreeItem item })
        {
            // WPF has already toggled IsChecked via the TwoWay binding;
            // nothing extra needed — just let the setischecked cascade run.
            // If it was null (indeterminate) and IsThreeState=False, WPF set it to true. ✓
            _ = item; // suppress unused warning
        }
    }

    // ── Merge & Save ─────────────────────────────────────────────────
    private async void BtnMerge_Click(object sender, RoutedEventArgs e)
    {
        if (_selected.Count == 0) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Merged File",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".txt",
            FileName = "merged.txt"
        };
        if (dlg.ShowDialog() != true) return;

        var outputPath = dlg.FileName;
        var filesToMerge = _selected.ToList();

        BtnMerge.IsEnabled = false;
        SetStatus($"Merging {filesToMerge.Count} file(s)…");

        try
        {
            await Task.Run(() => WriteOutput(filesToMerge, outputPath));
            SetStatus($"✓  Saved → {outputPath}");

            var open = MessageBox.Show(
                $"Merged {filesToMerge.Count} file(s) successfully!\n\nOpen the output file?",
                "Done", MessageBoxButton.YesNo, MessageBoxImage.Information);



            if (open == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(
                   new System.Diagnostics.ProcessStartInfo(outputPath) { UseShellExecute = true });
        }

        catch (Exception ex)
        {
            SetStatus($"❌ Error: {ex.Message}");
            MessageBox.Show($"Failed to merge files:\n{ex.Message}",
                      "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnMerge.IsEnabled = _selected.Count > 0;
        }
    }

    private static void WriteOutput(IList<SelectedFileItem> files, string outputPath)
    {
        var sb = new StringBuilder();

        foreach (var f in files)
        {
            if (!File.Exists(f.FullPath)) continue;

            var rel = f.RelativePath.Replace('\\', '/');
            sb.AppendLine();
            sb.AppendLine("//========================");
            sb.Append(rel).AppendLine(":");
            sb.AppendLine("//========================");
            sb.AppendLine(File.ReadAllText(f.FullPath, Encoding.UTF8));
        }

        File.WriteAllText(outputPath, sb.ToString().TrimStart('\r', '\n'), Encoding.UTF8);
    }

    // ── Helpers ──────────────────────────────────────────────────────
    private void RefreshRootDisplay()
    {
        if (_rootFolder is null)
        {
            TxtRootPath.Text = "No folder selected — click Browse to start";
            TxtRootPath.Foreground = (System.Windows.Media.Brush)FindResource("TxtMuted");
        }
        else
        {
            TxtRootPath.Text = _rootFolder;
            TxtRootPath.Foreground = (System.Windows.Media.Brush)FindResource("TxtPrimary");
        }
    }

    // Restore a file selection after loading cache / refreshing tree
    private void ApplyFileCheck(string path, bool check)
        => FindAndSetCheck(_tree, path, check);

    private static void FindAndSetCheck(
        IEnumerable<FileTreeItem> items, string path, bool check)
    {
        foreach (var item in items)
        {
            if (!item.IsDirectory &&
                item.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                item.SetIsChecked(check, updateChildren: false, updateParent: true);
                return;
            }
            FindAndSetCheck(item.Children, path, check);
        }
    }

    private void InsertSorted(SelectedFileItem si)
    {
        int idx = _selected.ToList().FindIndex(x =>
            string.Compare(x.RelativePath, si.RelativePath,
                StringComparison.OrdinalIgnoreCase) > 0);
        if (idx < 0) _selected.Add(si);
        else _selected.Insert(idx, si);
    }

    private SelectedFileItem MakeSelectedItem(string fullPath)
    {
        var rel = Path.GetRelativePath(_rootFolder!, fullPath);
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        return new SelectedFileItem
        {
            FullPath = fullPath,
            RelativePath = rel,
            Icon = ext switch { ".cs" => "⚙", ".json" => "{}", _ => "≡" }
        };
    }

    private void UpdateFooter()
    {
        int count = _selected.Count;
        TxtSelectedCount.Text = count.ToString();
        BtnMerge.IsEnabled = count > 0;

        if (count == 0)
        {
            TxtSizeInfo.Text = "No files selected";
        }
        else
        {
            long bytes = _selectedPaths
                .Where(File.Exists)
                .Sum(p => { try { return new FileInfo(p).Length; } catch { return 0L; } });
            TxtSizeInfo.Text = $"{count} file(s)   ·   {FormatBytes(bytes)} total";
        }
    }

    private void SetStatus(string msg) =>
        Dispatcher.InvokeAsync(() => TxtStatus.Text = msg);

    private static int CountFiles(FileTreeItem node) =>
        node.IsDirectory ? node.Children.Sum(CountFiles) : 1;

    private static string FormatBytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1_048_576 => $"{b / 1024.0:F1} KB",
        _ => $"{b / 1_048_576.0:F1} MB"
    };
}
