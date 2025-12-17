using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FluentIcons.Common;
using Disc.Analyzer.Models;

namespace Disc.Analyzer.ViewModels;

public static class Converters
{
    public static readonly IValueConverter IsDirectoryToIcon = new FuncValueConverter<bool, StreamGeometry>(isDir =>
    {
        var path = isDir
            ? "M19 20H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h6l2 2h6a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2z"
            : "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z";
        return StreamGeometry.Parse(path);
    });

    public static readonly IValueConverter IsDirectoryToColor = new FuncValueConverter<bool, IBrush>(isDir =>
        isDir ? new SolidColorBrush(Color.Parse("#3B82F6")) : new SolidColorBrush(Color.Parse("#6B7280")));

    public static readonly IValueConverter HighlightToBackground = new FuncValueConverter<bool, IBrush>(isHighlighted =>
        isHighlighted ? new SolidColorBrush(Color.Parse("#FEF08A")) : Brushes.Transparent);  // Yellow highlight

    public static readonly IValueConverter PercentageToWidth = new FuncValueConverter<double, double>(percentage =>
    {
        // Bar container is 120px wide, with 8px margins on each side = 104px usable
        const double maxWidth = 104;
        return Math.Max(0, Math.Min(maxWidth, percentage / 100 * maxWidth));
    });

    public static readonly IValueConverter PercentageToBarWidth = new FuncValueConverter<double, double>(percentage =>
    {
        // For a 288px wide container (320 - 32 padding)
        const double maxWidth = 256;
        return Math.Max(0, Math.Min(maxWidth, percentage / 100 * maxWidth));
    });

    public static readonly IValueConverter PercentageToDriveBarWidth = new FuncValueConverter<double, double>(percentage =>
    {
        // For drive panel (280 - 24 padding - 16 button padding = 240)
        const double maxWidth = 240;
        return Math.Max(0, Math.Min(maxWidth, percentage / 100 * maxWidth));
    });

    public static readonly IValueConverter PercentageToSmallBarWidth = new FuncValueConverter<double, double>(percentage =>
    {
        // For horizontal drive cards (156px width - same as bar width in template)
        const double maxWidth = 156;
        return Math.Max(0, Math.Min(maxWidth, percentage / 100 * maxWidth));
    });

    public static readonly IValueConverter PercentageToCompactBarWidth = new FuncValueConverter<double, double>(percentage =>
    {
        // For compact horizontal drive cards (120px width)
        const double maxWidth = 120;
        return Math.Max(0, Math.Min(maxWidth, percentage / 100 * maxWidth));
    });

    public static readonly IValueConverter PercentageToBarColor = new FuncValueConverter<double, IBrush>(percentage =>
    {
        // Green when low, yellow when medium, red when high
        if (percentage < 70)
            return new SolidColorBrush(Color.Parse("#10B981")); // Green
        if (percentage < 90)
            return new SolidColorBrush(Color.Parse("#F59E0B")); // Yellow/Orange
        return new SolidColorBrush(Color.Parse("#EF4444")); // Red
    });

    public static readonly IValueConverter IsSelectedToBackground = new FuncValueConverter<bool, IBrush>(isSelected =>
        isSelected 
            ? new SolidColorBrush(Color.Parse("#E5E7EB")) // Light gray when selected
            : Brushes.Transparent);
    
    // Category converters
    public static readonly IValueConverter IsCategoryMain = new FuncValueConverter<DriveCategory, bool>(
        category => category == DriveCategory.Main);
    
    public static readonly IValueConverter IsCategoryExternal = new FuncValueConverter<DriveCategory, bool>(
        category => category == DriveCategory.External);
    
    public static readonly IValueConverter IsCategoryNetwork = new FuncValueConverter<DriveCategory, bool>(
        category => category == DriveCategory.Network);
    
    public static readonly IValueConverter IsCategorySystem = new FuncValueConverter<DriveCategory, bool>(
        category => category == DriveCategory.System);
    
    // Expand/collapse icon converter
    public static readonly IMultiValueConverter BoolToExpandIcon = new BoolToExpandIconConverter();
}

public class BoolToExpandIconConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is bool isExpanded)
        {
            return isExpanded ? Symbol.ChevronUp : Symbol.ChevronDown;
        }
        return Symbol.ChevronDown;
    }
}
