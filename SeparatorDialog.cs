using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FileStitcher.Models;

namespace FileStitcher;

public class SeparatorDialog : Window
{
    // ── Outputs ──────────────────────────────────────────────────────
    public SeparatorType SelectedType { get; private set; }
    public int EmptyLineCount { get; private set; }
    public string CustomTemplate { get; private set; } = string.Empty;

    // ── Controls ─────────────────────────────────────────────────────
    private readonly RadioButton _rbNone, _rbEmptyLine, _rbHeader, _rbCustom;
    private readonly Border _cardNone, _cardEmptyLine, _cardHeader, _cardCustom;
    private readonly TextBox _tbLineCount, _tbCustom;
    private readonly FrameworkElement _emptyExtras, _customExtras;

    // ════════════════════════════════════════════════════════════════
    public SeparatorDialog(SeparatorType currentType, int currentLineCount, string currentCustom)
    {
        Title = "Separator Settings";
        Width = 530;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 740;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = C("#1A1B2E");
        FontFamily = new FontFamily("Segoe UI");

        var radioStyle = BuildRadioStyle();

        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };

        // heading
        root.Children.Add(Tb("Separator Settings", 16, FontWeights.SemiBold, "#E2E8F0"));
        root.Children.Add(Tb("Choose how files are separated in the merged output.",
                              12, FontWeights.Normal, "#475569",
                              margin: new Thickness(0, 3, 0, 18)));

        // ── None ─────────────────────────────────────────────────────
        _rbNone = Radio("None", radioStyle);
        (_cardNone, _) = Card(_rbNone,
            "Files are concatenated with no separator whatsoever.", null);
        root.Children.Add(_cardNone);

        // ── Empty Lines ───────────────────────────────────────────────
        _tbLineCount = InputBox(currentLineCount.ToString(), 56);
        var emptyRow = new StackPanel { Orientation = Orientation.Horizontal };

        var tb = Tb("Number of empty lines:",
            size: 12,
            w: FontWeights.Normal,
            hex: "#94A3B8",
            margin: new Thickness(0, 0, 8, 0));

        tb.VerticalAlignment = VerticalAlignment.Center;

        emptyRow.Children.Add(tb);

        emptyRow.Children.Add(_tbLineCount);
        _emptyExtras = emptyRow;

        _rbEmptyLine = Radio("Empty Lines", radioStyle);
        (_cardEmptyLine, _) = Card(_rbEmptyLine,
            "Inserts one or more blank lines between each file.", _emptyExtras);
        root.Children.Add(_cardEmptyLine);

        // ── Header Block ──────────────────────────────────────────────
        var preview = new Border
        {
            Background = C("#0D0D1F"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            BorderBrush = C("#2D2D4E"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "//========================\n{{RelativePath}}:\n//========================",
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 11,
                Foreground = C("#A78BFA")
            }
        };
        _rbHeader = Radio("Header Block", radioStyle);
        (_cardHeader, _) = Card(_rbHeader,
            "Wraps each file with a comment block showing its path (current default).", preview);
        root.Children.Add(_cardHeader);

        // ── Custom ────────────────────────────────────────────────────
        _tbCustom = new TextBox
        {
            Text = currentCustom,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 84,
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 12,
            Padding = new Thickness(10, 7, 10, 7),
            Background = C("#0D0D1F"),
            Foreground = C("#E2E8F0"),
            BorderBrush = C("#7C3AED"),
            BorderThickness = new Thickness(1),
            CaretBrush = C("#A78BFA"),
            SelectionBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x7C, 0x3A, 0xED)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var customInner = new StackPanel();
        customInner.Children.Add(_tbCustom);
        customInner.Children.Add(BuildVarReference());
        _customExtras = customInner;

        _rbCustom = Radio("Custom Template", radioStyle);
        (_cardCustom, _) = Card(_rbCustom,
            "Define your own separator template using the interpolation variables below.",
            _customExtras);
        root.Children.Add(_cardCustom);

        // ── Divider + Buttons ─────────────────────────────────────────
        root.Children.Add(new Border
        {
            Height = 1,
            Background = C("#2D2D4E"),
            Margin = new Thickness(0, 8, 0, 14)
        });

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = Btn("Cancel", "#2D2D4E", "#CBD5E1");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => DialogResult = false;

        var apply = Btn("Apply", "#7C3AED", "White");
        apply.Click += (_, _) =>
        {
            if (_rbCustom.IsChecked == true && string.IsNullOrWhiteSpace(_tbCustom.Text))
            { _tbCustom.Focus(); return; }
            Commit();
            DialogResult = true;
        };
        btnRow.Children.Add(cancel);
        btnRow.Children.Add(apply);
        root.Children.Add(btnRow);

        Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        // ── Wire radio events ─────────────────────────────────────────
        foreach (var rb in new[] { _rbNone, _rbEmptyLine, _rbHeader, _rbCustom })
            rb.Checked += (_, _) => SyncUI();

        (currentType switch
        {
            SeparatorType.None => _rbNone,
            SeparatorType.EmptyLine => _rbEmptyLine,
            SeparatorType.Custom => _rbCustom,
            _ => _rbHeader
        }).IsChecked = true;

        SyncUI();
    }

    // ════════════════════════════════════════════════════════════════
    //  SYNC
    // ════════════════════════════════════════════════════════════════

    private void SyncUI()
    {
        _emptyExtras.Visibility = _rbEmptyLine.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        _customExtras.Visibility = _rbCustom.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (card, rb) in new[]
        {
            (_cardNone, _rbNone), (_cardEmptyLine, _rbEmptyLine),
            (_cardHeader, _rbHeader), (_cardCustom, _rbCustom)
        })
        {
            bool on = rb.IsChecked == true;
            card.BorderBrush = on ? C("#7C3AED") : C("#2D2D4E");
            card.Background = on ? C("#1E1040") : C("#16213E");
        }
    }

    private void Commit()
    {
        SelectedType = _rbNone.IsChecked == true ? SeparatorType.None
                     : _rbEmptyLine.IsChecked == true ? SeparatorType.EmptyLine
                     : _rbCustom.IsChecked == true ? SeparatorType.Custom
                                                        : SeparatorType.Header;
        EmptyLineCount = int.TryParse(_tbLineCount.Text.Trim(), out int n) && n >= 1
                         ? Math.Min(n, 50) : 1;
        CustomTemplate = _tbCustom.Text;
    }

    // ════════════════════════════════════════════════════════════════
    //  LAYOUT HELPERS
    // ════════════════════════════════════════════════════════════════

    private (Border card, FrameworkElement? extras) Card(
        RadioButton rb, string description, FrameworkElement? extras)
    {
        var inner = new StackPanel();
        inner.Children.Add(rb);
        inner.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 11.5,
            Foreground = C("#64748B"),
            Margin = new Thickness(26, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        if (extras != null)
        {
            extras.Margin = new Thickness(26, 10, 0, 4);
            extras.Visibility = Visibility.Collapsed;
            inner.Children.Add(extras);
        }

        var card = new Border
        {
            Background = C("#16213E"),
            CornerRadius = new CornerRadius(8),
            BorderBrush = C("#2D2D4E"),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = inner
        };
        return (card, extras);
    }

    private static RadioButton Radio(string label, Style style) => new()
    {
        Content = new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        },
        GroupName = "sep",
        Style = style,
        Cursor = System.Windows.Input.Cursors.Hand
    };

    // ── Variable reference table ─────────────────────────────────────
    private static FrameworkElement BuildVarReference()
    {
        var border = new Border
        {
            Background = C("#12122A"),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 10, 12, 10),
            BorderBrush = C("#2D2D4E"),
            BorderThickness = new Thickness(1)
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = "Available interpolation variables",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = C("#64748B"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        static void Row(StackPanel p, string token, string desc)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var t = new TextBlock
            {
                Text = token,
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 11,
                Foreground = C("#A78BFA"),
                VerticalAlignment = VerticalAlignment.Top
            };
            var d = new TextBlock
            {
                Text = desc,
                FontSize = 11,
                Foreground = C("#64748B"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(t, 0);
            Grid.SetColumn(d, 1);
            g.Children.Add(t);
            g.Children.Add(d);
            p.Children.Add(g);
        }

        Row(sp, "{{FileNumber}}", "sequential number of the file in the list (1, 2, 3…)");
        Row(sp, "{{FileName}}", "file name with extension  (e.g.  Program.cs)");
        Row(sp, "{{RelativePath}}", "path relative to root folder  (e.g.  src/Utils.cs)");
        Row(sp, "{{FullPath}}", "absolute path on disk");

        border.Child = sp;
        return border;
    }

    // ── Custom RadioButton ControlTemplate ───────────────────────────
    private static Style BuildRadioStyle()
    {
        var template = new ControlTemplate(typeof(RadioButton));

        var rootGrid = new FrameworkElementFactory(typeof(Grid));

        var c0 = new FrameworkElementFactory(typeof(ColumnDefinition));
        c0.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
        var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
        c1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
        rootGrid.AppendChild(c0);
        rootGrid.AppendChild(c1);

        // Indicator: 16×16 grid with outer ring + inner dot
        var indicGrid = new FrameworkElementFactory(typeof(Grid));
        indicGrid.SetValue(Grid.ColumnProperty, 0);
        indicGrid.SetValue(FrameworkElement.WidthProperty, 16.0);
        indicGrid.SetValue(FrameworkElement.HeightProperty, 16.0);
        indicGrid.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        indicGrid.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));

        var outer = new FrameworkElementFactory(typeof(Ellipse));
        outer.Name = "outer";
        outer.SetValue(Ellipse.WidthProperty, 16.0);
        outer.SetValue(Ellipse.HeightProperty, 16.0);
        outer.SetValue(Shape.StrokeProperty, C("#5B5B8A"));
        outer.SetValue(Shape.StrokeThicknessProperty, 1.5);
        outer.SetValue(Shape.FillProperty, C("#1A1B2E"));

        var inner = new FrameworkElementFactory(typeof(Ellipse));
        inner.Name = "inner";
        inner.SetValue(Ellipse.WidthProperty, 8.0);
        inner.SetValue(Ellipse.HeightProperty, 8.0);
        inner.SetValue(Shape.FillProperty, C("#7C3AED"));
        inner.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        inner.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        inner.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);

        indicGrid.AppendChild(outer);
        indicGrid.AppendChild(inner);

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(Grid.ColumnProperty, 1);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

        rootGrid.AppendChild(indicGrid);
        rootGrid.AppendChild(cp);
        template.VisualTree = rootGrid;

        // Trigger: checked state
        var t = new Trigger { Property = RadioButton.IsCheckedProperty, Value = true };
        t.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "inner"));
        t.Setters.Add(new Setter(Shape.StrokeProperty, C("#7C3AED"), "outer"));
        t.Setters.Add(new Setter(Shape.FillProperty, C("#1E1040"), "outer"));
        template.Triggers.Add(t);

        var style = new Style(typeof(RadioButton));
        style.Setters.Add(new Setter(RadioButton.TemplateProperty, template));
        return style;
    }

    // ── Generic helpers ──────────────────────────────────────────────
    private static TextBlock Tb(string text, double size, FontWeight w, string hex,
                                Thickness? margin = null)
    {
        var tb = new TextBlock { Text = text, FontSize = size, FontWeight = w, Foreground = C(hex) };
        if (margin.HasValue) tb.Margin = margin.Value;
        return tb;
    }

    private static TextBox InputBox(string text, double width) => new()
    {
        Text = text,
        Width = width,
        FontSize = 13,
        Padding = new Thickness(8, 4, 8, 4),
        Background = C("#0D0D1F"),
        Foreground = C("#E2E8F0"),
        BorderBrush = C("#7C3AED"),
        BorderThickness = new Thickness(1),
        CaretBrush = C("#A78BFA"),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Button Btn(string text, string bg, string fg) => new()
    {
        Content = text,
        Width = 82,
        Height = 34,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Foreground = C(fg),
        Background = C(bg),
        BorderThickness = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private static SolidColorBrush C(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}