using System.Collections.ObjectModel;
using Disc.Analyzer.Models;

namespace Disc.Analyzer.ViewModels;

/// <summary>
/// Provides mock data for the XAML designer
/// </summary>
public class DesignTimeData
{
    public static MainWindowViewModel MainViewModel { get; } = CreateMainViewModel();

    private static MainWindowViewModel CreateMainViewModel()
    {
        var vm = new MainWindowViewModel();
        
        // Create mock tree structure
        var root = new FileSystemNode
        {
            Name = "Users",
            FullPath = "/Users/developer/",
            IsDirectory = true,
            Size = 50_000_000_000, // 50 GB
            FileCount = 125000,
            FolderCount = 8500,
            SizePercentage = 100,
            IsExpanded = true
        };

        var documents = CreateMockFolder("Documents", "/Users/developer/Documents/", 15_000_000_000, 48, 35000, root);
        var downloads = CreateMockFolder("Downloads", "/Users/developer/Downloads/", 8_500_000_000, 27, 1250, root);
        var applications = CreateMockFolder("Applications", "/Users/developer/Applications/", 12_000_000_000, 38, 45000, root);
        var pictures = CreateMockFolder("Pictures", "/Users/developer/Pictures/", 6_200_000_000, 20, 8500, root);
        var music = CreateMockFolder("Music", "/Users/developer/Music/", 4_100_000_000, 13, 12000, root);
        var desktop = CreateMockFolder("Desktop", "/Users/developer/Desktop/", 850_000_000, 3, 156, root);
        var cache = CreateMockFolder(".cache", "/Users/developer/.cache/", 2_100_000_000, 7, 18500, root);
        var config = CreateMockFolder(".config", "/Users/developer/.config/", 450_000_000, 1, 2400, root);

        // Add some nested folders
        CreateMockFolder("Projects", "/Users/developer/Documents/Projects/", 8_000_000_000, 53, 22000, documents);
        CreateMockFolder("Work", "/Users/developer/Documents/Work/", 4_500_000_000, 30, 8500, documents);
        CreateMockFolder("Personal", "/Users/developer/Documents/Personal/", 2_500_000_000, 17, 4500, documents);

        root.Children.Add(documents);
        root.Children.Add(downloads);
        root.Children.Add(applications);
        root.Children.Add(pictures);
        root.Children.Add(music);
        root.Children.Add(desktop);
        root.Children.Add(cache);
        root.Children.Add(config);

        vm.TreeItems.Add(root);

        // Mock update available
        vm.UpdateAvailable = true;
        vm.UpdateVersion = "1.0.5";
        vm.UpdateReady = true;

        return vm;
    }

    private static FileSystemNode CreateMockFolder(string name, string path, long size, double percentage, int fileCount, FileSystemNode parent)
    {
        return new FileSystemNode
        {
            Name = name,
            FullPath = path,
            IsDirectory = true,
            Size = size,
            FileCount = fileCount,
            FolderCount = fileCount / 50,
            SizePercentage = percentage,
            IsExpanded = false,
            Parent = parent
        };
    }

    // For binding to TreeItems directly in design mode
    public ObservableCollection<FileSystemNode> TreeItems => MainViewModel.TreeItems;
}
