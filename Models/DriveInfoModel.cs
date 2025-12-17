using System;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Disc.Analyzer.Models;

public enum DriveCategory
{
    Main,      // Primary user drives (/, /Users, main disk)
    External,  // USB, external drives
    Network,   // Network shares
    System     // System volumes, virtual drives
}

public partial class DriveInfoModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _volumeLabel = string.Empty;

    [ObservableProperty]
    private string _rootPath = string.Empty;

    [ObservableProperty]
    private long _totalSize;

    [ObservableProperty]
    private long _freeSpace;

    [ObservableProperty]
    private long _usedSpace;

    [ObservableProperty]
    private double _usedPercentage;

    [ObservableProperty]
    private DriveType _driveType;

    [ObservableProperty]
    private bool _isReady;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private DriveCategory _category;

    public string DisplayName => !string.IsNullOrEmpty(VolumeLabel) 
        ? $"{VolumeLabel} ({Name})" 
        : Name;
    
    /// <summary>
    /// Short display name - just the volume name without path prefixes
    /// </summary>
    public string ShortName
    {
        get
        {
            // Use volume label if available and different from path
            if (!string.IsNullOrEmpty(VolumeLabel) && !VolumeLabel.Contains('/')) 
                return VolumeLabel;
            
            // For root "/" just show "/"
            if (RootPath == "/") return "/";
            
            // For paths like /Volumes/Backup 5TB, show just "Backup 5TB"
            var path = RootPath.TrimEnd(Path.DirectorySeparatorChar);
            var lastSlash = path.LastIndexOf(Path.DirectorySeparatorChar);
            if (lastSlash >= 0 && lastSlash < path.Length - 1)
            {
                return path[(lastSlash + 1)..];
            }
            
            return Name;
        }
    }
    
    /// <summary>
    /// The mount point or parent path (e.g., "/Volumes" or "/System/Volumes")
    /// </summary>
    public string MountPoint
    {
        get
        {
            if (RootPath == "/") return "Root";
            
            var lastSep = RootPath.LastIndexOf(Path.DirectorySeparatorChar);
            if (lastSep > 0)
            {
                var parent = RootPath[..lastSep];
                // Simplify common prefixes
                return parent switch
                {
                    "/Volumes" => "Volumes",
                    "/System/Volumes" => "System",
                    "/mnt" => "mnt",
                    _ => parent.TrimStart(Path.DirectorySeparatorChar)
                };
            }
            return string.Empty;
        }
    }

    public string CategoryIcon => Category switch
    {
        DriveCategory.Main => "Home",
        DriveCategory.External => "UsbStick",
        DriveCategory.Network => "Cloud",
        DriveCategory.System => "Settings",
        _ => "Storage"
    };

    public string FormattedTotalSize => FileSystemNode.FormatSize(TotalSize);
    public string FormattedFreeSpace => FileSystemNode.FormatSize(FreeSpace);
    public string FormattedUsedSpace => FileSystemNode.FormatSize(UsedSpace);

    public string FreeSpaceText => $"{FormattedFreeSpace} free";

    public static DriveInfoModel FromDriveInfo(DriveInfo drive)
    {
        var model = new DriveInfoModel
        {
            Name = drive.Name.TrimEnd(Path.DirectorySeparatorChar),
            RootPath = drive.RootDirectory.FullName,
            DriveType = drive.DriveType,
            IsReady = drive.IsReady
        };

        if (drive.IsReady)
        {
            model.VolumeLabel = drive.VolumeLabel;
            model.TotalSize = drive.TotalSize;
            model.FreeSpace = drive.AvailableFreeSpace;
            model.UsedSpace = drive.TotalSize - drive.AvailableFreeSpace;
            model.UsedPercentage = drive.TotalSize > 0 
                ? (double)model.UsedSpace / drive.TotalSize * 100 
                : 0;
        }

        // Categorize the drive
        model.Category = CategorizeVolume(model);
        
        return model;
    }

    private static DriveCategory CategorizeVolume(DriveInfoModel drive)
    {
        var path = drive.RootPath.ToLowerInvariant();
        
        // Network drives
        if (drive.DriveType == DriveType.Network)
            return DriveCategory.Network;
        
        // Removable drives (USB, etc.)
        if (drive.DriveType == DriveType.Removable)
            return DriveCategory.External;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || 
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // macOS/Linux system volumes
            if (path.Contains("/system/volumes/") ||
                path.Contains("/private/") ||
                path == "/dev" ||
                path.StartsWith("/dev/") ||
                path.Contains("/snap/") ||
                path.Contains("/proc") ||
                path.Contains("/sys") ||
                path.Contains("/run") ||
                path.Contains("/boot") && !path.Equals("/boot") ||
                path.Contains("/var/vm") ||
                path.Contains("/preboot") ||
                path.Contains("/recovery") ||
                path.Contains("/update") ||
                path.Contains("/vm"))
            {
                return DriveCategory.System;
            }
            
            // Main drives on Unix
            if (path == "/" || path.StartsWith("/users") || path.StartsWith("/home") || 
                path.StartsWith("/volumes/") && !path.Contains("preboot") && !path.Contains("recovery"))
            {
                return DriveCategory.Main;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: C: is main, others could be external
            if (path.StartsWith("c:"))
                return DriveCategory.Main;
        }
        
        // Default: treat fixed drives as main, others as external
        return drive.DriveType == DriveType.Fixed ? DriveCategory.Main : DriveCategory.External;
    }
}
