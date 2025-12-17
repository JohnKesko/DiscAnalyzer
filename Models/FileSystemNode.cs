using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Disc.Analyzer.Models;

public partial class FileSystemNode : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private long _size;

    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private int _folderCount;

    [ObservableProperty]
    private DateTime _lastModified;

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private bool _isExpanded;  // Default false - we control expansion in scanner

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isHighlighted;  // For search results

    [ObservableProperty]
    private double _sizePercentage;

    [ObservableProperty]
    private FileSystemNode? _parent;

    public ObservableCollection<FileSystemNode> Children { get; } = new();

    public string FormattedSize => FormatSize(Size);

    public string FileCountText => IsDirectory ? $"{FileCount:N0} files" : "1 file";

    public static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return suffixIndex == 0 
            ? $"{size:N0} {suffixes[suffixIndex]}" 
            : $"{size:N1} {suffixes[suffixIndex]}";
    }

    public void UpdateSizePercentage(long parentSize)
    {
        SizePercentage = parentSize > 0 ? (double)Size / parentSize * 100 : 0;
    }

    /// <summary>
    /// Recalculate percentage based on current parent size
    /// </summary>
    public void RecalculatePercentage()
    {
        if (Parent != null && Parent.Size > 0)
        {
            SizePercentage = (double)Size / Parent.Size * 100;
        }
        else if (Parent == null)
        {
            // Root node is always 100%
            SizePercentage = 100;
        }
    }

    public void NotifySizeChanged()
    {
        OnPropertyChanged(nameof(FormattedSize));
        OnPropertyChanged(nameof(FileCountText));
        RecalculatePercentage();
    }
}
