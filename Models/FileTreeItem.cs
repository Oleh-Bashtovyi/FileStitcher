using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace FileStitcher.Models;

public class FileTreeItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public FileTreeItem? Parent { get; set; }
    public ObservableCollection<FileTreeItem> Children { get; } = [];

    private bool? _isChecked = false;

    public bool? IsChecked
    {
        get => _isChecked;
        set => SetIsChecked(value, updateChildren: true, updateParent: true);
    }

    public string Icon => IsDirectory ? "📁" : Path.GetExtension(FullPath).ToLowerInvariant() switch
    {
        ".cs"   => "⚙",
        ".json" => "{}",
        ".txt"  => "≡",
        _       => "•"
    };

    // Internal method so parent/child updates don't trigger full cascade loops
    public void SetIsChecked(bool? value, bool updateChildren, bool updateParent)
    {
        if (value == _isChecked) return;

        _isChecked = value;

        if (updateChildren && _isChecked.HasValue)
            foreach (var child in Children)
                child.SetIsChecked(_isChecked, updateChildren: true, updateParent: false);

        if (updateParent)
            Parent?.VerifyCheckedState();

        OnPropertyChanged(nameof(IsChecked));
    }

    // Called by a child when its state changes — recalculates this node's state
    private void VerifyCheckedState()
    {
        if (Children.Count == 0)
        {
            SetIsChecked(false, updateChildren: false, updateParent: true);
            return;
        }

        bool? commonState = Children[0].IsChecked;
        foreach (var child in Children.Skip(1))
        {
            if (child.IsChecked != commonState)
            {
                commonState = null; // indeterminate
                break;
            }
        }

        SetIsChecked(commonState, updateChildren: false, updateParent: true);
    }

    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
