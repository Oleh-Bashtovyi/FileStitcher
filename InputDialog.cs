using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileStitcher;

public class InputDialog : Window
{
    private readonly TextBox _input;
    public string Result => _input.Text.Trim();

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x3B));
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        root.Children.Add(new TextBlock
        {
            Text = prompt,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _input = new TextBox
        {
            Text = defaultValue,
            FontSize = 13,
            Padding = new Thickness(10, 7, 10, 7),
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x21, 0x3E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA)),
            SelectionBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x7C, 0x3A, 0xED)),
            Margin = new Thickness(0, 0, 0, 16)
        };
        _input.SelectAll();
        root.Children.Add(_input);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var btnCancel = MakeButton("Cancel", "#2D2D4E", "#CBD5E1");
        btnCancel.Margin = new Thickness(0, 0, 8, 0);
        btnCancel.Click += (_, _) => { DialogResult = false; };

        var btnOk = MakeButton("Save", "#7C3AED", "White");
        btnOk.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_input.Text)) return;
            DialogResult = true;
        };

        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnOk);
        root.Children.Add(btnRow);

        Content = root;

        // Enter / Escape shortcuts
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(_input.Text)) DialogResult = true;
            if (e.Key == Key.Escape) DialogResult = false;
        };

        Loaded += (_, _) => { _input.Focus(); };
    }

    private static Button MakeButton(string text, string bg, string fg)
    {
        return new Button
        {
            Content = text,
            Width = 80,
            Height = 34,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg)),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = BuildTemplate()
        };
    }

    private static ControlTemplate BuildTemplate()
    {
        // Minimal rounded button template
        var tpl = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.PaddingProperty, new Thickness(0));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        tpl.VisualTree = border;
        return tpl;
    }
}