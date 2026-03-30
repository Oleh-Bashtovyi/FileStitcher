using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileStitcher;

public class EditPresetDialog : Window
{
    private readonly TextBox _nameBox;
    private readonly TextBox _rootBox;

    public string PresetName => _nameBox.Text.Trim();
    public string RootFolder => _rootBox.Text.Trim();

    public EditPresetDialog(string currentName, string currentRoot)
    {
        Title = "Edit Preset";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#22223B");
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        // ── Name ──────────────────────────────────────────────────
        root.Children.Add(Label("Preset Name"));
        _nameBox = InputBox(currentName);
        root.Children.Add(_nameBox);

        // ── Root Folder ───────────────────────────────────────────
        root.Children.Add(Label("Root Folder", topMargin: 14));

        var folderRow = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _rootBox = InputBox(currentRoot);
        _rootBox.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(_rootBox, 0);

        var browseBtn = MakeButton("📁 Browse", "#2D2D4E", "#CBD5E1", width: 90);
        browseBtn.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select Root Folder" };
            if (dlg.ShowDialog() == true)
                _rootBox.Text = dlg.FolderName;
        };
        Grid.SetColumn(browseBtn, 1);

        folderRow.Children.Add(_rootBox);
        folderRow.Children.Add(browseBtn);
        root.Children.Add(folderRow);

        // ── Buttons ───────────────────────────────────────────────
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancel = MakeButton("Cancel", "#2D2D4E", "#CBD5E1", width: 80);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => DialogResult = false;

        var save = MakeButton("Save", "#7C3AED", "White", width: 80);
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text)) { _nameBox.Focus(); return; }
            if (!Directory.Exists(_rootBox.Text))
            {
                MessageBox.Show("The folder does not exist.", "Invalid Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        };

        btnRow.Children.Add(cancel);
        btnRow.Children.Add(save);
        root.Children.Add(btnRow);

        Content = root;

        _nameBox.KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        _rootBox.KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        Loaded += (_, _) => { _nameBox.Focus(); _nameBox.SelectAll(); };
    }

    // ── Helpers ──────────────────────────────────────────────────────
    private static TextBlock Label(string text, double topMargin = 0) => new()
    {
        Text = text,
        Foreground = Brush("#94A3B8"),
        FontSize = 12,
        Margin = new Thickness(0, topMargin, 0, 6)
    };

    private static TextBox InputBox(string value) => new()
    {
        Text = value,
        FontSize = 13,
        Padding = new Thickness(10, 7, 10, 7),
        Background = Brush("#16213E"),
        Foreground = Brush("#E2E8F0"),
        BorderBrush = Brush("#7C3AED"),
        BorderThickness = new Thickness(1),
        CaretBrush = Brush("#A78BFA"),
        SelectionBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x7C, 0x3A, 0xED)),
        Margin = new Thickness(0, 0, 0, 0)
    };

    private static Button MakeButton(string text, string bg, string fg,
                                     double width = 80) => new()
                                     {
                                         Content = text,
                                         Width = width,
                                         Height = 34,
                                         FontSize = 13,
                                         FontWeight = FontWeights.SemiBold,
                                         Foreground = Brush(fg),
                                         Background = Brush(bg),
                                         BorderThickness = new Thickness(0),
                                         Cursor = Cursors.Hand
                                     };

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}