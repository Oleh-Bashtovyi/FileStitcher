using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileStitcher.Models;
using FileStitcher.Services;

namespace FileStitcher;

public partial class MainWindow : Window
{
    // ── Supported extensions ────────────────────────────────────────
    private static readonly HashSet<string> SupportedExt =
        new(StringComparer.OrdinalIgnoreCase) { ".cs", ".txt", ".json" };

    private static readonly HashSet<string> SkipDirs =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules", ".idea" };

    // ── State ───────────────────────────────────────────────────────
    private readonly CacheService _cacheService = new();
    private AppCache _appCache = new();

    private readonly ObservableCollection<FileTreeItem> _tree = [];
    private readonly ObservableCollection<SelectedFileItem> _selected = [];
    private readonly HashSet<string> _selectedPaths =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _rootFolder;
    private string? _activePresetId;
    private bool _bulkOp;

    // ────────────────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();
        FileTreeView.ItemsSource = _tree;
        SelectedFilesList.ItemsSource = _selected;
        LoadCache();
    }

    // ════════════════════════════════════════════════════════════════
    //  CACHE
    // ════════════════════════════════════════════════════════════════

    private void LoadCache()
    {
        _appCache = _cacheService.Load() ?? new AppCache();
        RebuildPresetChips();

        // Restore the last active preset
        var active = _appCache.Presets
            .FirstOrDefault(p => p.Id == _appCache.ActivePresetId);

        if (active != null)
            LoadPreset(active, announce: false);
    }

    private void SaveCache()
    {
        _cacheService.Save(_appCache);
    }

    // Sync current selection back into the active preset and persist
    private void FlushActivePreset()
    {
        if (_activePresetId is null) return;
        var preset = _appCache.Presets.FirstOrDefault(p => p.Id == _activePresetId);
        if (preset is null) return;
        preset.SelectedFiles = [.. _selectedPaths];
        SaveCache();
    }

    // ════════════════════════════════════════════════════════════════
    //  PRESET CHIPS UI
    // ════════════════════════════════════════════════════════════════

    private void RebuildPresetChips()
    {
        PresetChipsPanel.Children.Clear();

        if (_appCache.Presets.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No presets yet — browse a folder and save one",
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            PresetChipsPanel.Children.Add(empty);
            return;
        }

        foreach (var preset in _appCache.Presets)
        {
            bool isActive = preset.Id == _activePresetId;
            var chip = BuildChip(preset, isActive);
            PresetChipsPanel.Children.Add(chip);
        }
    }

    private Border BuildChip(Preset preset, bool isActive)
    {
        // Chip text
        var label = new TextBlock
        {
            Text = preset.Name,
            Foreground = isActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            FontSize = 12,
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 160,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = $"{preset.RootFolder}\n{preset.SelectedFiles.Count} file(s)"
        };

        // Delete button
        var btnDelete = new Button
        {
            Content = "✕",
            Width = 16,
            Height = 16,
            FontSize = 9,
            Foreground = isActive
                ? new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(6, 0, 0, 0),
            Tag = preset.Id,
            ToolTip = "Delete preset"
        };
        btnDelete.Click += BtnDeletePreset_Click;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(label);
        row.Children.Add(btnDelete);

        var chip = new Border
        {
            Background = isActive
                ? new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED))
                : new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x4E)),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(12, 5, 8, 5),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = row,
            Tag = preset.Id
        };
        chip.MouseLeftButtonDown += Chip_Click;
        return chip;
    }

    private void Chip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string id }) return;
        var preset = _appCache.Presets.FirstOrDefault(p => p.Id == id);
        if (preset is null) return;
        LoadPreset(preset, announce: true);
    }

    private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't bubble to chip click
        if (sender is not Button { Tag: string id }) return;

        var preset = _appCache.Presets.FirstOrDefault(p => p.Id == id);
        if (preset is null) return;

        var result = MessageBox.Show(
            $"Delete preset \"{preset.Name}\"?",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _appCache.Presets.Remove(preset);

        if (_activePresetId == id)
        {
            _activePresetId = null;
            _appCache.ActivePresetId = null;

            // Clear the workspace
            _bulkOp = true;
            _tree.Clear(); _selected.Clear(); _selectedPaths.Clear();
            _rootFolder = null;
            _bulkOp = false;
            RefreshRootDisplay();
            UpdateFooter();
        }

        SaveCache();
        RebuildPresetChips();
        SetStatus($"Preset \"{preset.Name}\" deleted.");
    }

    // ════════════════════════════════════════════════════════════════
    //  PRESET LOAD / SAVE
    // ════════════════════════════════════════════════════════════════

    private void LoadPreset(Preset preset, bool announce)
    {
        // If root no longer exists — remove preset, ask user
        if (!Directory.Exists(preset.RootFolder))
        {
            MessageBox.Show(
                $"The root folder for preset \"{preset.Name}\" no longer exists:\n{preset.RootFolder}\n\nThe preset will be removed.",
                "Folder Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            _appCache.Presets.Remove(preset);
            if (_activePresetId == preset.Id) _activePresetId = null;
            SaveCache();
            RebuildPresetChips();
            return;
        }

        _activePresetId = preset.Id;
        _appCache.ActivePresetId = preset.Id;
        _rootFolder = preset.RootFolder;

        // Clear current state
        _bulkOp = true;
        _tree.Clear(); _selected.Clear(); _selectedPaths.Clear();

        RefreshRootDisplay();
        BuildTree();

        // Restore files, skip missing
        var missing = new List<string>();
        foreach (var path in preset.SelectedFiles)
        {
            if (File.Exists(path)) ApplyFileCheck(path, check: true);
            else missing.Add(path);
        }

        // Prune missing from preset
        if (missing.Count > 0)
        {
            missing.ForEach(p => preset.SelectedFiles.Remove(p));
            SaveCache();
        }

        _bulkOp = false;
        UpdateFooter();
        RebuildPresetChips();
        BtnSavePreset.IsEnabled = true;

        if (announce) SetStatus($"Loaded preset \"{preset.Name}\" · {(missing.Count > 0 ? $"{missing.Count} missing file(s) removed" : "OK")}");
    }

    private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_rootFolder is null) return;

        // Save into existing active preset
        if (_activePresetId is not null)
        {
            FlushActivePreset();
            SetStatus("Preset saved.");
            RebuildPresetChips();
            return;
        }

        // No active preset — treat as new
        SaveNewPreset();
    }

    private void BtnNewPreset_Click(object sender, RoutedEventArgs e)
    {
        // Browse new folder first if none loaded
        if (_rootFolder is null)
        {
            BtnBrowse_Click(sender, e);
            if (_rootFolder is null) return;
        }
        SaveNewPreset();
    }

    private void SaveNewPreset()
    {
        if (_rootFolder is null) return;

        var defaultName = new DirectoryInfo(_rootFolder).Name;
        var dlg = new InputDialog("New Preset", "Enter a name for this preset:", defaultName)
        {
            Owner = this
        };
        if (dlg.ShowDialog() != true) return;

        var preset = new Preset
        {
            Name = dlg.Result,
            RootFolder = _rootFolder,
            SelectedFiles = [.. _selectedPaths]
        };

        _appCache.Presets.Add(preset);
        _activePresetId = preset.Id;
        _appCache.ActivePresetId = preset.Id;
        SaveCache();
        RebuildPresetChips();
        BtnSavePreset.IsEnabled = true;
        SetStatus($"Preset \"{preset.Name}\" created.");
    }

    // ════════════════════════════════════════════════════════════════
    //  TREE
    // ════════════════════════════════════════════════════════════════

    private void BuildTree()
    {
        if (_rootFolder is null) return;
        _tree.Clear();
        try
        {
            var root = BuildNode(new DirectoryInfo(_rootFolder), parent: null);
            if (root != null) _tree.Add(root);
        }
        catch (UnauthorizedAccessException) { }

        int total = _tree.Sum(CountFiles);
        TxtTreeStatus.Text = total > 0
            ? $"{total} supported file(s) in scope"
            : "No .cs / .json / .txt files found";
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

        IEnumerable<DirectoryInfo> subDirs;
        try { subDirs = dir.GetDirectories().Where(d => !SkipDirs.Contains(d.Name)).OrderBy(d => d.Name); }
        catch { subDirs = []; }

        foreach (var sub in subDirs)
        {
            var child = BuildNode(sub, node);
            if (child != null) node.Children.Add(child);
        }

        IEnumerable<FileInfo> files;
        try { files = dir.GetFiles().Where(f => SupportedExt.Contains(f.Extension)).OrderBy(f => f.Name); }
        catch { files = []; }

        foreach (var f in files)
        {
            var fi = new FileTreeItem
            {
                Name = f.Name,
                FullPath = f.FullName,
                IsDirectory = false,
                Parent = node
            };
            fi.PropertyChanged += OnFileItemPropertyChanged;
            node.Children.Add(fi);
        }

        return node.Children.Count > 0 ? node : null;
    }

    // ════════════════════════════════════════════════════════════════
    //  CHECK / UNCHECK
    // ════════════════════════════════════════════════════════════════

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

        if (!_bulkOp) { UpdateFooter(); FlushActivePreset(); }
    }

    private void TreeCheckBox_Click(object sender, RoutedEventArgs e) { /* cascade runs via TwoWay binding */ }

    // ════════════════════════════════════════════════════════════════
    //  BUTTON HANDLERS
    // ════════════════════════════════════════════════════════════════

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select Root Folder" };
        if (dlg.ShowDialog() != true) return;

        _rootFolder = dlg.FolderName;
        RefreshRootDisplay();

        _bulkOp = true;
        _tree.Clear(); _selected.Clear(); _selectedPaths.Clear();
        _activePresetId = null;
        _bulkOp = false;

        BuildTree();
        UpdateFooter();
        RebuildPresetChips(); // deselect active chip
        BtnSavePreset.IsEnabled = true;
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

        var saved = _selectedPaths.ToList();

        _bulkOp = true;
        _tree.Clear(); _selected.Clear(); _selectedPaths.Clear();
        BuildTree();
        foreach (var path in saved.Where(File.Exists)) ApplyFileCheck(path, check: true);
        _bulkOp = false;

        UpdateFooter();
        FlushActivePreset();
        SetStatus("Tree refreshed.");
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        _bulkOp = true;
        foreach (var item in _tree) item.SetIsChecked(true, updateChildren: true, updateParent: false);
        _bulkOp = false;
        UpdateFooter(); FlushActivePreset();
    }

    private void BtnClearAll_Click(object sender, RoutedEventArgs e)
    {
        _bulkOp = true;
        foreach (var item in _tree) item.SetIsChecked(false, updateChildren: true, updateParent: false);
        _bulkOp = false;
        UpdateFooter(); FlushActivePreset();
    }

    private void BtnRemoveAll_Click(object sender, RoutedEventArgs e) =>
        BtnClearAll_Click(sender, e);

    private void BtnRemoveFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            FindAndSetCheck(_tree, path, check: false);
    }

    // ════════════════════════════════════════════════════════════════
    //  MERGE
    // ════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════

    private void RefreshRootDisplay()
    {
        if (_rootFolder is null)
        {
            TxtRootPath.Text = "No folder selected";
            TxtRootPath.Foreground = (Brush)FindResource("TxtMuted");
        }
        else
        {
            TxtRootPath.Text = _rootFolder;
            TxtRootPath.Foreground = (Brush)FindResource("TxtPrimary");
        }
    }

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
        int idx = _selected.ToList()
            .FindIndex(x => string.Compare(x.RelativePath, si.RelativePath,
                StringComparison.OrdinalIgnoreCase) > 0);
        if (idx < 0) _selected.Add(si);
        else _selected.Insert(idx, si);
    }

    private SelectedFileItem MakeSelectedItem(string fullPath) =>
        new()
        {
            FullPath = fullPath,
            RelativePath = Path.GetRelativePath(_rootFolder!, fullPath),
            Icon = Path.GetExtension(fullPath).ToLowerInvariant() switch
            { ".cs" => "⚙", ".json" => "{}", _ => "≡" }
        };

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
            long bytes = _selectedPaths.Where(File.Exists)
                .Sum(p => { try { return new FileInfo(p).Length; } catch { return 0L; } });
            TxtSizeInfo.Text = $"{count} file(s)   ·   {FormatBytes(bytes)} total";
        }
    }

    private void SetStatus(string msg) =>
        Dispatcher.InvokeAsync(() => TxtStatus.Text = msg);

    private static int CountFiles(FileTreeItem n) =>
        n.IsDirectory ? n.Children.Sum(CountFiles) : 1;

    private static string FormatBytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1_048_576 => $"{b / 1024.0:F1} KB",
        _ => $"{b / 1_048_576.0:F1} MB"
    };
}