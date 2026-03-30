using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FileStitcher.Models;
using FileStitcher.Services;

namespace FileStitcher;

public partial class MainWindow : Window
{
    // ── Defaults ────────────────────────────────────────────────────
    private static readonly List<string> DefaultExtensions = [".cs", ".txt", ".json"];

    private static readonly HashSet<string> SkipDirs =
        new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", ".vs", "node_modules", ".idea" };

    // ── Persistence ─────────────────────────────────────────────────
    private readonly CacheService _cacheService = new();
    private AppCache _appCache = new();

    // ── Collections ─────────────────────────────────────────────────
    private readonly ObservableCollection<FileTreeItem> _tree = [];
    private readonly ObservableCollection<SelectedFileItem> _selected = [];
    private readonly HashSet<string> _selectedPaths =
        new(StringComparer.OrdinalIgnoreCase);

    // ── Session state ───────────────────────────────────────────────
    private string? _rootFolder;
    private string? _activePresetId;
    private bool _bulkOp;

    // Working extension set (rebuilt from active preset or defaults)
    private HashSet<string> _currentExtensions =
        new(DefaultExtensions, StringComparer.OrdinalIgnoreCase);

    // ── Drag-drop state ─────────────────────────────────────────────
    private Point _dragStartPoint;
    private SelectedFileItem? _draggedItem;
    private int _dropIndex = -1;

    // ════════════════════════════════════════════════════════════════
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

        var active = _appCache.Presets.FirstOrDefault(p => p.Id == _appCache.ActivePresetId);
        if (active != null) LoadPreset(active, announce: false);
        else RebuildExtTags(); // show defaults even with no preset
    }

    private void SaveCache() => _cacheService.Save(_appCache);

    private void FlushActivePreset()
    {
        if (_activePresetId is null) return;
        var preset = _appCache.Presets.FirstOrDefault(p => p.Id == _activePresetId);
        if (preset is null) return;

        preset.SelectedFiles = [.. _selectedPaths];
        preset.Extensions = [.. _currentExtensions];
        SaveCache();
    }

    // ════════════════════════════════════════════════════════════════
    //  EXTENSIONS
    // ════════════════════════════════════════════════════════════════

    private void RebuildExtTags()
    {
        ExtTagsPanel.Children.Clear();
        foreach (var ext in _currentExtensions.OrderBy(e => e))
        {
            var tag = BuildExtTag(ext);
            ExtTagsPanel.Children.Add(tag);
        }
    }

    private Border BuildExtTag(string ext)
    {
        var label = new TextBlock
        {
            Text = ext,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA)),
            FontSize = 11,
            FontFamily = new FontFamily("Consolas, Courier New"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var btnRemove = new Button
        {
            Content = "✕",
            Width = 14,
            Height = 14,
            FontSize = 8,
            Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(5, 0, 0, 0),
            Tag = ext
        };
        btnRemove.Click += (_, _) =>
        {
            if (_currentExtensions.Count <= 1)
            {
                SetStatus("At least one extension must remain.");
                return;
            }
            _currentExtensions.Remove(ext);
            RebuildExtTags();
            RebuildTree();
            FlushActivePreset();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(label);
        row.Children.Add(btnRemove);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x3A)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(9, 3, 6, 3),
            Margin = new Thickness(0, 0, 5, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x6E)),
            BorderThickness = new Thickness(1),
            Child = row,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void TxtNewExt_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TxtNewExt.Text == ".ext") TxtNewExt.SelectAll();
    }

    private void TxtNewExt_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddExtensionFromInput();
    }

    private void BtnAddExt_Click(object sender, RoutedEventArgs e) =>
        AddExtensionFromInput();

    private void AddExtensionFromInput()
    {
        var raw = TxtNewExt.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw) || raw == ".ext") return;

        var ext = raw.StartsWith('.') ? raw : "." + raw;
        if (_currentExtensions.Contains(ext)) { SetStatus($"{ext} is already in the list."); return; }

        _currentExtensions.Add(ext);
        TxtNewExt.Text = ".ext";
        RebuildExtTags();
        RebuildTree();
        FlushActivePreset();
    }

    private void BtnResetExt_Click(object sender, RoutedEventArgs e)
    {
        _currentExtensions = new HashSet<string>(DefaultExtensions, StringComparer.OrdinalIgnoreCase);
        RebuildExtTags();
        RebuildTree();
        FlushActivePreset();
        SetStatus("Extensions reset to defaults.");
    }

    // ════════════════════════════════════════════════════════════════
    //  PRESET CHIPS
    // ════════════════════════════════════════════════════════════════

    private void RebuildPresetChips()
    {
        PresetChipsPanel.Children.Clear();

        if (_appCache.Presets.Count == 0)
        {
            PresetChipsPanel.Children.Add(new TextBlock
            {
                Text = "No presets yet",
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        foreach (var preset in _appCache.Presets)
            PresetChipsPanel.Children.Add(BuildChip(preset));
    }

    private Border BuildChip(Preset preset)
    {
        bool active = preset.Id == _activePresetId;
        var textFg = active ? Colors.White : Color.FromRgb(0xCB, 0xD5, 0xE1);
        var bg = active ? Color.FromRgb(0x7C, 0x3A, 0xED)
                             : Color.FromRgb(0x2D, 0x2D, 0x4E);

        var nameLabel = new TextBlock
        {
            Text = preset.Name,
            Foreground = new SolidColorBrush(textFg),
            FontSize = 12,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 150,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = $"{preset.RootFolder}\n{preset.SelectedFiles.Count} file(s) · {string.Join(", ", preset.Extensions)}"
        };

        // ✏ edit button
        var btnEdit = ChipBtn("✏", preset.Id, BtnEditPreset_Click, active);
        btnEdit.ToolTip = "Rename or change root folder";

        // ✕ delete button
        var btnDel = ChipBtn("✕", preset.Id, BtnDeletePreset_Click, active);
        btnDel.ToolTip = "Delete preset";

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(nameLabel);
        row.Children.Add(btnEdit);
        row.Children.Add(btnDel);

        var chip = new Border
        {
            Background = new SolidColorBrush(bg),
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

    private static Button ChipBtn(string icon, string tag,
                                   RoutedEventHandler handler, bool active)
    {
        var fg = active
            ? Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)
            : Color.FromRgb(0x64, 0x74, 0x8B);
        var btn = new Button
        {
            Content = icon,
            Width = 16,
            Height = 16,
            FontSize = 9,
            Foreground = new SolidColorBrush(fg),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(5, 0, 0, 0),
            Tag = tag
        };
        btn.Click += handler;
        return btn;
    }

    private void Chip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string id }) return;
        var preset = _appCache.Presets.FirstOrDefault(p => p.Id == id);
        if (preset is null) return;
        LoadPreset(preset, announce: true);
    }

    private void BtnEditPreset_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: string id }) return;
        var preset = _appCache.Presets.FirstOrDefault(p => p.Id == id);
        if (preset is null) return;

        var dlg = new EditPresetDialog(preset.Name, preset.RootFolder) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        bool rootChanged = !preset.RootFolder.Equals(dlg.RootFolder, StringComparison.OrdinalIgnoreCase);

        preset.Name = dlg.PresetName;
        preset.RootFolder = dlg.RootFolder;
        SaveCache();
        RebuildPresetChips();

        // If this is the active preset and the root changed, reload
        if (_activePresetId == id && rootChanged)
        {
            _rootFolder = preset.RootFolder;
            RefreshRootDisplay();
            RebuildTree();
            SetStatus($"Preset root changed — tree reloaded.");
        }
        else
        {
            SetStatus($"Preset renamed to \"{ preset.Name}\".");
        }
    }

    private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: string id }) return;
        var preset = _appCache.Presets.FirstOrDefault(p => p.Id == id);
        if (preset is null) return;

        if (MessageBox.Show($"Delete preset \"{preset.Name}\"?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _appCache.Presets.Remove(preset);

        if (_activePresetId == id)
        {
            _activePresetId = null; _appCache.ActivePresetId = null;
            _bulkOp = true;
            _tree.Clear(); _selected.Clear(); _selectedPaths.Clear();
            _rootFolder = null;
            _bulkOp = false;
            RefreshRootDisplay(); UpdateFooter();
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
        if (!Directory.Exists(preset.RootFolder))
        {
            MessageBox.Show(
                $"Root folder for preset \"{preset.Name}\" no longer exists:\n{preset.RootFolder}\n\nThe preset will be removed.",
                "Folder Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            _appCache.Presets.Remove(preset);
            if (_activePresetId == preset.Id) _activePresetId = null;
            SaveCache(); RebuildPresetChips();
            return;
        }

        _activePresetId = preset.Id;
        _appCache.ActivePresetId = preset.Id;
        _rootFolder = preset.RootFolder;

        // Restore extensions (handle old cache without Extensions field)
        _currentExtensions = preset.Extensions.Count > 0
            ? new HashSet<string>(preset.Extensions, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(DefaultExtensions, StringComparer.OrdinalIgnoreCase);
        RebuildExtTags();

        _bulkOp = true;
        _tree.Clear(); _selected.Clear(); _selectedPaths.Clear();

        RefreshRootDisplay();
        BuildTree();

        var missing = new List<string>();
        foreach (var path in preset.SelectedFiles)
        {
            if (File.Exists(path)) ApplyFileCheck(path, check: true);
            else missing.Add(path);
        }

        if (missing.Count > 0) { missing.ForEach(p => preset.SelectedFiles.Remove(p)); SaveCache(); }

        _bulkOp = false;
        UpdateFooter(); RebuildPresetChips();
        BtnSavePreset.IsEnabled = true;

        if (announce)
            SetStatus($"Loaded \"{preset.Name}\"" +
                      (missing.Count > 0 ? $" — {missing.Count} missing file(s) removed" : ""));
    }

    private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_rootFolder is null) return;

        if (_activePresetId is not null) { FlushActivePreset(); SetStatus("Preset saved."); RebuildPresetChips(); return; }
        SaveNewPreset();
    }

    private void BtnNewPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_rootFolder is null) { BtnBrowse_Click(sender, e); if (_rootFolder is null) return; }
        SaveNewPreset();
    }

    private void SaveNewPreset()
    {
        if (_rootFolder is null) return;
        var defaultName = new DirectoryInfo(_rootFolder).Name;
        var dlg = new InputDialog("New Preset", "Enter a name for this preset:", defaultName) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var preset = new Preset
        {
            Name = dlg.Result,
            RootFolder = _rootFolder,
            SelectedFiles = [.. _selectedPaths],
            Extensions = [.. _currentExtensions]
        };

        _appCache.Presets.Add(preset);
        _activePresetId = preset.Id;
        _appCache.ActivePresetId = preset.Id;
        SaveCache(); RebuildPresetChips();
        BtnSavePreset.IsEnabled = true;
        SetStatus($"Preset \"{preset.Name}\" created.");
    }

    // ════════════════════════════════════════════════════════════════
    //  TREE
    // ════════════════════════════════════════════════════════════════

    private void RebuildTree()
    {
        if (_rootFolder is null) return;
        var savedPaths = _selectedPaths.ToList();

        _bulkOp = true;
        _tree.Clear(); _selected.Clear(); _selectedPaths.Clear();
        BuildTree();
        foreach (var path in savedPaths.Where(File.Exists)) ApplyFileCheck(path, check: true);
        _bulkOp = false;

        UpdateFooter();
    }

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
            : $"No {string.Join("/", _currentExtensions)} files found";
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

        IEnumerable<DirectoryInfo> subs;
        try { subs = dir.GetDirectories().Where(d => !SkipDirs.Contains(d.Name)).OrderBy(d => d.Name); }
        catch { subs = []; }

        foreach (var sub in subs)
        {
            var child = BuildNode(sub, node);
            if (child != null) node.Children.Add(child);
        }

        IEnumerable<FileInfo> files;
        try { files = dir.GetFiles().Where(f => _currentExtensions.Contains(f.Extension)).OrderBy(f => f.Name); }
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
                _selected.Add(MakeSelectedItem(item.FullPath));  // ← append to end
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

    private void TreeCheckBox_Click(object sender, RoutedEventArgs e) { }

    // ════════════════════════════════════════════════════════════════
    //  DRAG-AND-DROP (right panel reorder)
    // ════════════════════════════════════════════════════════════════

    private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement el) return;

        var pos = e.GetPosition(null);
        var delta = pos - _dragStartPoint;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;

        if (el.DataContext is not SelectedFileItem item) return;
        _draggedItem = item;

        DragDrop.DoDragDrop(el, item, DragDropEffects.Move);

        // Cleanup after drop
        _draggedItem = null;
        _dropIndex = -1;
        DropLine.Visibility = Visibility.Collapsed;
    }

    private void SelectedList_DragOver(object sender, DragEventArgs e)
    {
        if (_draggedItem is null) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Move;

        var posInGrid = e.GetPosition(RightContentGrid);
        _dropIndex = CalcDropIndex(posInGrid, out double lineY);

        // Show and position the insertion line
        DropLine.Visibility = Visibility.Visible;
        DropLine.Width = Math.Max(0, RightContentGrid.ActualWidth - 20);
        System.Windows.Controls.Canvas.SetTop(DropLine, lineY - 1);

        e.Handled = true;
    }

    private void SelectedList_Drop(object sender, DragEventArgs e)
    {
        if (_draggedItem is null) return;

        var posInGrid = e.GetPosition(RightContentGrid);
        int targetIdx = CalcDropIndex(posInGrid, out _);

        int fromIdx = _selected.IndexOf(_draggedItem);
        if (fromIdx < 0 || fromIdx == targetIdx) return;

        // When moving down, the effective target shifts by -1 after removal
        int insertAt = targetIdx > fromIdx ? targetIdx - 1 : targetIdx;
        insertAt = Math.Clamp(insertAt, 0, _selected.Count - 1);

        _selected.Move(fromIdx, insertAt);
        FlushActivePreset();

        DropLine.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void SelectedList_DragLeave(object sender, DragEventArgs e)
    {
        // Only hide if actually leaving the ScrollViewer bounds
        var pos = e.GetPosition(SelectedScrollViewer);
        if (pos.X < 0 || pos.Y < 0 ||
            pos.X > SelectedScrollViewer.ActualWidth ||
            pos.Y > SelectedScrollViewer.ActualHeight)
        {
            DropLine.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Returns the insert-before index for the given mouse position,
    /// and the Y coordinate (relative to RightContentGrid) to draw the line.
    /// </summary>
    private int CalcDropIndex(Point posInGrid, out double lineY)
    {
        lineY = 0;
        for (int i = 0; i < _selected.Count; i++)
        {
            var container = SelectedFilesList.ItemContainerGenerator
                                             .ContainerFromIndex(i) as FrameworkElement;
            if (container is null) continue;

            var topLeft = container.TranslatePoint(new Point(0, 0), RightContentGrid);
            var midY = topLeft.Y + container.ActualHeight / 2;

            if (posInGrid.Y < midY)
            {
                lineY = topLeft.Y;
                return i;
            }
            lineY = topLeft.Y + container.ActualHeight + 4;
        }
        return _selected.Count;
    }

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
        _activePresetId = null; _appCache.ActivePresetId = null;
        _bulkOp = false;

        BuildTree();
        UpdateFooter();
        RebuildPresetChips();
        BtnSavePreset.IsEnabled = true;
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_rootFolder is null || !Directory.Exists(_rootFolder))
        {
            MessageBox.Show("Root folder no longer exists. Please select a new one.",
                "Missing Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            _rootFolder = null; RefreshRootDisplay();
            _bulkOp = true;
            _tree.Clear(); _selected.Clear(); _selectedPaths.Clear();
            _bulkOp = false;
            UpdateFooter();
            return;
        }
        RebuildTree();
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

            if (MessageBox.Show($"Merged {filesToMerge.Count} file(s).\n\nOpen the output file?",
                    "Done", MessageBoxButton.YesNo, MessageBoxImage.Information)
                == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(outputPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"❌ {ex.Message}");
            MessageBox.Show($"Failed to merge:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { BtnMerge.IsEnabled = _selected.Count > 0; }
    }

    private static void WriteOutput(IList<SelectedFileItem> files, string outputPath)
    {
        File.WriteAllText(outputPath, BuildMergedString(files), Encoding.UTF8);
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

    private SelectedFileItem MakeSelectedItem(string fullPath) => new()
    {
        FullPath = fullPath,
        RelativePath = System.IO.Path.GetRelativePath(_rootFolder!, fullPath),
        Icon = System.IO.Path.GetExtension(fullPath).ToLowerInvariant() switch
        { ".cs" => "⚙", ".json" => "{}", ".md" => "#", _ => "≡" }
    };

    private void UpdateFooter()
    {
        int count = _selected.Count;
        TxtSelectedCount.Text = count.ToString();
        BtnMerge.IsEnabled = count > 0;
        BtnCopy.IsEnabled = count > 0;

        if (count == 0) { TxtSizeInfo.Text = "No files selected"; return; }

        long bytes = _selectedPaths.Where(File.Exists)
            .Sum(p => { try { return new FileInfo(p).Length; } catch { return 0L; } });
        TxtSizeInfo.Text = $"{count} file(s)   ·   {FormatBytes(bytes)} total";
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

    private async void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_selected.Count == 0) return;

        var filesToCopy = _selected.ToList();
        BtnCopy.IsEnabled = false;
        SetStatus($"Copying {filesToCopy.Count} file(s)…");

        try
        {
            var text = await Task.Run(() => BuildMergedString(filesToCopy));
            Clipboard.SetText(text);
            SetStatus($"✓  Copied {filesToCopy.Count} file(s) to clipboard " +
                      $"({FormatBytes(Encoding.UTF8.GetByteCount(text))})");
        }
        catch (Exception ex)
        {
            SetStatus($"❌ {ex.Message}");
        }
        finally { BtnCopy.IsEnabled = _selected.Count > 0; }
    }

    private static string BuildMergedString(IList<SelectedFileItem> files)
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
        return sb.ToString().TrimStart('\r', '\n');
    }
}