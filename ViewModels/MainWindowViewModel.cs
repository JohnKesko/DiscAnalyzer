using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Disc.Analyzer.Models;
using Disc.Analyzer.Services;

namespace Disc.Analyzer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private CancellationTokenSource? _scanCts;
    private readonly Stopwatch _scanStopwatch = new();
    private DirectoryScanner? _currentScanner;
    private readonly SettingsService _settingsService = new();
    private readonly UpdateService _updateService;
    
    // Throttling for UI updates - increased interval for large scans
    private readonly Stopwatch _lastUiUpdate = new();
    private const int UiUpdateIntervalMs = 200; // Update UI max 5 times per second
    private readonly object _pendingUpdatesLock = new();
    private readonly HashSet<FileSystemNode> _pendingNodeUpdates = new();
    
    // Batched node additions - collect nodes and add in batches
    private readonly ConcurrentQueue<(FileSystemNode Parent, FileSystemNode Child)> _pendingNodeAdditions = new();
    private volatile bool _isProcessingBatch = false;

    [ObservableProperty]
    private ScanSettings _settings = new();

    [ObservableProperty]
    private FileSystemNode? _rootNode;

    [ObservableProperty]
    private FileSystemNode? _selectedNode;

    [ObservableProperty]
    private DriveInfoModel? _selectedDrive;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string _updateVersion = string.Empty;

    [ObservableProperty]
    private bool _updateReady;

    public string AppVersion => $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

    [ObservableProperty]
    private bool _isScanning;
    
    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private string _statusText = "Select a drive or folder to scan";

    [ObservableProperty]
    private string _progressText = string.Empty;

    public ObservableCollection<DriveInfoModel> Drives { get; } = new();
    
    // Settings properties bound to UI
    public bool SettingsShowFiles
    {
        get => _settingsService.Settings.ShowFiles;
        set
        {
            if (_settingsService.Settings.ShowFiles != value)
            {
                _settingsService.UpdateAndSave(s => s.ShowFiles = value);
                Settings.ShowFiles = value;
                OnPropertyChanged();
            }
        }
    }
    
    public int SettingsDefaultExpansionLevel
    {
        get => _settingsService.Settings.DefaultExpansionLevel;
        set
        {
            if (_settingsService.Settings.DefaultExpansionLevel != value)
            {
                _settingsService.UpdateAndSave(s => s.DefaultExpansionLevel = value);
                OnPropertyChanged();
            }
        }
    }
    
    public int SettingsMaxParallelism
    {
        get => _settingsService.Settings.MaxParallelism;
        set
        {
            if (_settingsService.Settings.MaxParallelism != value)
            {
                _settingsService.UpdateAndSave(s => s.MaxParallelism = value);
                Settings.MaxParallelism = value;
                OnPropertyChanged();
            }
        }
    }

    public bool SettingsAutoUpdate
    {
        get => _settingsService.Settings.AutoUpdate;
        set
        {
            if (_settingsService.Settings.AutoUpdate != value)
            {
                _settingsService.UpdateAndSave(s => s.AutoUpdate = value);
                OnPropertyChanged();
            }
        }
    }

    public MainWindowViewModel()
    {
        // Initialize update service
        _updateService = new UpdateService(_settingsService);
        _updateService.UpdateAvailable += version =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateVersion = version;
                UpdateAvailable = true;
            });
        };
        _updateService.UpdateReady += () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateReady = true;
            });
        };
        
        // Load settings from JSON file
        LoadSettingsToModel();
        LoadDrives();
        
        // Load last scanned path if available
        if (!string.IsNullOrEmpty(_settingsService.Settings.LastScannedPath) &&
            Directory.Exists(_settingsService.Settings.LastScannedPath))
        {
            StatusText = $"Last scanned: {_settingsService.Settings.LastScannedPath}";
        }
        
        // Check for updates in background
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000); // Wait 2 seconds after startup
            await _updateService.CheckForUpdatesAsync();
        });
    }

    [RelayCommand]
    private void ApplyUpdate()
    {
        _updateService.ApplyUpdateAndRestart();
    }
    
    private void LoadSettingsToModel()
    {
        Settings.ShowFiles = _settingsService.Settings.ShowFiles;
        Settings.MaxParallelism = _settingsService.Settings.MaxParallelism;
        ShowSystemVolumes = _settingsService.Settings.ShowSystemVolumes;
        ExpansionLevel = _settingsService.Settings.DefaultExpansionLevel;
    }
    
    [RelayCommand]
    private void OpenSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
    }
    
    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    private void LoadDrives()
    {
        Drives.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                var model = DriveInfoModel.FromDriveInfo(drive);
                Drives.Add(model);
            }
            catch
            {
                // Skip inaccessible drives
            }
        }
        NotifyDriveCollectionsChanged();
    }
    
    private void NotifyDriveCollectionsChanged()
    {
        OnPropertyChanged(nameof(MainDrives));
        OnPropertyChanged(nameof(SystemDrives));
        OnPropertyChanged(nameof(SystemDrivesCount));
        OnPropertyChanged(nameof(SystemDrivesText));
    }
    
    [RelayCommand]
    private void ToggleSystemVolumes()
    {
        ShowSystemVolumes = !ShowSystemVolumes;
        _settingsService.UpdateAndSave(s => s.ShowSystemVolumes = ShowSystemVolumes);
    }

    [RelayCommand]
    private void RefreshDrives()
    {
        LoadDrives();
    }

    [RelayCommand]
    private async Task ScanDriveAsync(DriveInfoModel? drive)
    {
        if (drive == null || !drive.IsReady) return;
        
        // Update selection
        foreach (var d in Drives) d.IsSelected = false;
        drive.IsSelected = true;
        SelectedDrive = drive;
        
        await ScanFolderAsync(drive.RootPath);
    }

    [ObservableProperty]
    private int _foldersScanned;

    [ObservableProperty]
    private int _filesScanned;

    [ObservableProperty]
    private long _bytesScanned;

    [ObservableProperty]
    private string _scanDuration = string.Empty;

    [ObservableProperty]
    private string _lastScanned = string.Empty;

    [ObservableProperty]
    private FileSystemNode? _largestFolder;

    [ObservableProperty]
    private string _searchText = string.Empty;
    
    private List<FileSystemNode> _searchResults = new();
    private int _currentSearchIndex = -1;

    partial void OnSearchTextChanged(string value)
    {
        // Clear previous highlights
        ClearSearchHighlights();
        
        if (string.IsNullOrWhiteSpace(value) || RootNode == null)
        {
            _searchResults.Clear();
            _currentSearchIndex = -1;
            return;
        }
        
        // Find all matching nodes
        _searchResults = FindMatchingNodes(RootNode, value).ToList();
        _currentSearchIndex = -1;
        
        // Jump to first result
        if (_searchResults.Count > 0)
        {
            JumpToNextSearchResult();
        }
    }

    [RelayCommand]
    private void SearchNext()
    {
        if (_searchResults.Count > 0)
        {
            JumpToNextSearchResult();
        }
    }

    [RelayCommand]
    private void SearchPrevious()
    {
        if (_searchResults.Count > 0)
        {
            JumpToPreviousSearchResult();
        }
    }

    private void JumpToNextSearchResult()
    {
        if (_searchResults.Count == 0) return;
        
        // Clear previous selection highlight
        if (_currentSearchIndex >= 0 && _currentSearchIndex < _searchResults.Count)
        {
            _searchResults[_currentSearchIndex].IsHighlighted = false;
        }
        
        _currentSearchIndex = (_currentSearchIndex + 1) % _searchResults.Count;
        var node = _searchResults[_currentSearchIndex];
        
        // Expand parents to make visible (but not the node itself)
        ExpandParents(node);
        
        // Highlight and select
        node.IsHighlighted = true;
        SelectedNode = node;
    }

    private void JumpToPreviousSearchResult()
    {
        if (_searchResults.Count == 0) return;
        
        // Clear previous selection highlight
        if (_currentSearchIndex >= 0 && _currentSearchIndex < _searchResults.Count)
        {
            _searchResults[_currentSearchIndex].IsHighlighted = false;
        }
        
        _currentSearchIndex = _currentSearchIndex <= 0 ? _searchResults.Count - 1 : _currentSearchIndex - 1;
        var node = _searchResults[_currentSearchIndex];
        
        // Expand parents to make visible (but not the node itself)
        ExpandParents(node);
        
        // Highlight and select
        node.IsHighlighted = true;
        SelectedNode = node;
    }

    private void ExpandParents(FileSystemNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            parent.IsExpanded = true;
            parent = parent.Parent;
        }
    }

    private IEnumerable<FileSystemNode> FindMatchingNodes(FileSystemNode node, string searchText)
    {
        var search = searchText.ToLowerInvariant();
        
        if (node.Name.ToLowerInvariant().Contains(search))
        {
            yield return node;
        }
        
        foreach (var child in node.Children.ToList())
        {
            foreach (var match in FindMatchingNodes(child, searchText))
            {
                yield return match;
            }
        }
    }

    private void ClearSearchHighlights()
    {
        foreach (var node in _searchResults)
        {
            node.IsHighlighted = false;
        }
    }

    [ObservableProperty]
    private int _expansionLevel = 1;  // Current expansion depth (1 = show first level children)

    [ObservableProperty]
    private bool _showSystemVolumes = false;
    
    // Filtered drive collections
    public IEnumerable<DriveInfoModel> MainDrives => Drives
        .Where(d => d.Category != DriveCategory.System)
        .OrderByDescending(d => d.Category == DriveCategory.Main)
        .ThenBy(d => d.DisplayName);
    
    public IEnumerable<DriveInfoModel> SystemDrives => Drives
        .Where(d => d.Category == DriveCategory.System)
        .OrderBy(d => d.DisplayName);
    
    public int SystemDrivesCount => SystemDrives.Count();
    
    public string SystemDrivesText => $"{SystemDrivesCount} system volume{(SystemDrivesCount != 1 ? "s" : "")}";

    public ObservableCollection<FileSystemNode> TreeItems { get; } = new();

    public string ExpansionLevelText => $"Level {ExpansionLevel}";

    // Overview properties - use scanning progress for real-time updates
    public string TotalSizeFormatted => IsScanning 
        ? FileSystemNode.FormatSize(BytesScanned)
        : (RootNode != null ? FileSystemNode.FormatSize(RootNode.Size) : "0 B");

    public string TotalFilesFormatted => IsScanning 
        ? $"{FilesScanned:N0}"
        : (RootNode != null ? $"{RootNode.FileCount:N0}" : "0");

    public string TotalFoldersFormatted => IsScanning 
        ? $"{FoldersScanned:N0}"
        : (RootNode != null ? $"{RootNode.FolderCount:N0}" : "0");

    public string LargestFolderName => LargestFolder?.Name ?? "-";
    
    public string LargestFolderSize => LargestFolder != null 
        ? FileSystemNode.FormatSize(LargestFolder.Size) 
        : "";

    public double LargestFolderPercentage => LargestFolder?.SizePercentage ?? 0;

    [RelayCommand]
    private async Task SelectFolderAsync()
    {
        var topLevel = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;

        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder to analyze",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var path = folders[0].Path.LocalPath;
            await ScanFolderAsync(path);
        }
    }

    [RelayCommand]
    private async Task ScanFolderAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        // Cancel any existing scan
        await CancelScanAsync();

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        StatusText = $"Scanning: {path}";
        ProgressText = "Starting scan...";
        TreeItems.Clear();
        RootNode = null;
        LargestFolder = null;
        
        // Clear pending updates
        lock (_pendingUpdatesLock)
        {
            _pendingNodeUpdates.Clear();
        }

        _scanStopwatch.Restart();
        _lastUiUpdate.Restart();

        var scanner = new DirectoryScanner(Settings);
        _currentScanner = scanner;

        // Subscribe to events for real-time updates
        scanner.RootNodeCreated += OnRootNodeCreated;
        scanner.ProgressChanged += OnProgressChanged;
        scanner.NodeDiscovered += OnNodeDiscovered;
        scanner.NodeSizeCalculated += OnNodeSizeCalculated;

        try
        {
            // Run scanner in background - it will fire RootNodeCreated first
            var result = await Task.Run(
                () => scanner.ScanAsync(path, _scanCts.Token),
                _scanCts.Token);

            _scanStopwatch.Stop();

            if (result != null)
            {
                // Flush any remaining pending node additions
                await DrainPendingNodeAdditionsAsync();
                
                // Flush any remaining pending updates
                FlushPendingUpdates();
                
                // Final update on UI thread - the tree is already populated
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Final sort and percentage calculation
                    if (RootNode != null)
                    {
                        FinalizeTreeSorting(RootNode);
                        RootNode.SizePercentage = 100;
                        RootNode.NotifySizeChanged();
                        
                        // Apply expansion level from settings
                        ApplyExpansionLevel(RootNode, 0);
                        OnPropertyChanged(nameof(ExpansionLevelText));
                    }
                    
                    // Find largest immediate child folder
                    LargestFolder = result.Children
                        .Where(c => c.IsDirectory)
                        .OrderByDescending(c => c.Size)
                        .FirstOrDefault();

                    var finalProgress = scanner.GetCurrentProgress();
                    FoldersScanned = finalProgress.FoldersScanned;
                    FilesScanned = finalProgress.FilesScanned;
                    BytesScanned = finalProgress.BytesScanned;

                    ScanDuration = $"{_scanStopwatch.Elapsed.TotalSeconds:F1}s";
                    LastScanned = DateTime.Now.ToString("HH:mm:ss");
                    StatusText = $"Scan complete: {result.Name}";
                    ProgressText = $"Scanned {FoldersScanned:N0} folders, {FilesScanned:N0} files in {ScanDuration}";
                    
                    // Save last scanned path
                    _settingsService.UpdateAndSave(s => s.LastScannedPath = result.FullPath);

                    NotifyOverviewChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText = "Scan cancelled";
                ProgressText = "";
                _scanStopwatch.Stop();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText = $"Error: {ex.Message}";
                ProgressText = "";
            });
        }
        finally
        {
            // Unsubscribe from events
            scanner.RootNodeCreated -= OnRootNodeCreated;
            scanner.ProgressChanged -= OnProgressChanged;
            scanner.NodeDiscovered -= OnNodeDiscovered;
            scanner.NodeSizeCalculated -= OnNodeSizeCalculated;
            _currentScanner = null;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsScanning = false;
            });
        }
    }

    [RelayCommand]
    private async Task CancelScanAsync()
    {
        if (_scanCts != null)
        {
            await _scanCts.CancelAsync();
            _scanCts.Dispose();
            _scanCts = null;
        }
    }



    [RelayCommand]
    private void ToggleShowFiles()
    {
        SettingsShowFiles = !SettingsShowFiles;
        // Re-scan if we have a root node
        if (RootNode != null)
        {
            _ = ScanFolderAsync(RootNode.FullPath);
        }
    }

    [RelayCommand]
    private void ExpandLevel()
    {
        if (RootNode == null) return;
        ExpansionLevel++;
        ApplyExpansionLevel(RootNode, 0);
        OnPropertyChanged(nameof(ExpansionLevelText));
    }

    [RelayCommand]
    private void CollapseLevel()
    {
        if (RootNode == null || ExpansionLevel <= 0) return;
        ExpansionLevel--;
        ApplyExpansionLevel(RootNode, 0);
        OnPropertyChanged(nameof(ExpansionLevelText));
    }

    [RelayCommand]
    private void ExpandAll()
    {
        if (RootNode == null) return;
        ExpansionLevel = GetMaxDepth(RootNode, 0);
        ApplyExpansionLevel(RootNode, 0);
        OnPropertyChanged(nameof(ExpansionLevelText));
    }

    [RelayCommand]
    private void CollapseAll()
    {
        if (RootNode == null) return;
        ExpansionLevel = 0;
        ApplyExpansionLevel(RootNode, 0);
        OnPropertyChanged(nameof(ExpansionLevelText));
    }

    private void ApplyExpansionLevel(FileSystemNode node, int currentDepth)
    {
        // Root is always expanded, children depend on depth
        node.IsExpanded = currentDepth < ExpansionLevel;
        
        // Only recurse into children that are within the expansion range + 1
        // This avoids visiting deeply nested nodes unnecessarily, which could be A LOT
        if (currentDepth <= ExpansionLevel)
        {
            // Take snapshot to avoid collection modified during enumeration
            foreach (var child in node.Children.ToList())
            {
                if (child.IsDirectory)
                {
                    ApplyExpansionLevel(child, currentDepth + 1);
                }
            }
        }
    }

    private static int GetMaxDepth(FileSystemNode node, int currentDepth)
    {
        // Limit depth calculation to avoid slowness with millions of folders
        const int maxDepthLimit = 20;
        if (currentDepth >= maxDepthLimit) return currentDepth;
        
        int maxDepth = currentDepth;
        // Take snapshot to avoid collection modified during enumeration
        foreach (var child in node.Children.ToList())
        {
            if (child.IsDirectory && child.Children.Count > 0)
            {
                maxDepth = Math.Max(maxDepth, GetMaxDepth(child, currentDepth + 1));
            }
        }
        return maxDepth;
    }

    /// <summary>
    /// Calculate the depth of a node by walking up the parent chain
    /// </summary>
    private int GetNodeDepth(FileSystemNode node)
    {
        int depth = 0;
        var current = node;
        while (current.Parent != null)
        {
            depth++;
            current = current.Parent;
        }
        return depth;
    }

    /// <summary>
    /// Called from background thread when progress changes
    /// </summary>
    private void OnProgressChanged(ScanProgress progress)
    {
        FoldersScanned = progress.FoldersScanned;
        FilesScanned = progress.FilesScanned;
        BytesScanned = progress.BytesScanned;
        
        // Throttled UI update
        if (_lastUiUpdate.ElapsedMilliseconds >= UiUpdateIntervalMs)
        {
            FlushPendingUpdates();
            
            Dispatcher.UIThread.Post(() =>
            {
                ProgressText = $"Scanning... {FoldersScanned:N0} folders, {FilesScanned:N0} files";
                NotifyOverviewChanged();
            }, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Called from background thread when the root node is created
    /// </summary>
    private void OnRootNodeCreated(FileSystemNode rootNode)
    {
        // Post to UI thread to add root to tree
        Dispatcher.UIThread.Post(() =>
        {
            RootNode = rootNode;
            RootNode.IsExpanded = true; // Root always expanded
            TreeItems.Clear();
            TreeItems.Add(rootNode);
            _lastUiUpdate.Restart();
        });
    }

    /// <summary>
    /// Called from background thread when a new node is discovered
    /// </summary>
    private void OnNodeDiscovered(FileSystemNode parent, FileSystemNode child)
    {
        // Queue the addition instead of dispatching immediately
        _pendingNodeAdditions.Enqueue((parent, child));
        
        // Only process batch periodically to avoid UI overload
        if (!_isProcessingBatch && _lastUiUpdate.ElapsedMilliseconds >= UiUpdateIntervalMs)
        {
            ProcessPendingNodeAdditions();
        }
    }
    
    /// <summary>
    /// Process all pending node additions in a single batch
    /// </summary>
    private void ProcessPendingNodeAdditions()
    {
        if (_isProcessingBatch) return;
        _isProcessingBatch = true;
        
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // Process up to 500 additions per batch to keep UI responsive
                const int maxBatchSize = 500;
                int processed = 0;
                
                while (processed < maxBatchSize && _pendingNodeAdditions.TryDequeue(out var item))
                {
                    // Set expansion state based on depth and current expansion level
                    if (item.Child.IsDirectory)
                    {
                        int depth = GetNodeDepth(item.Child);
                        item.Child.IsExpanded = depth < ExpansionLevel;
                    }
                    
                    // Simple add at end - we'll sort after scan completes
                    item.Parent.Children.Add(item.Child);
                    processed++;
                }
                
                _lastUiUpdate.Restart();
            }
            finally
            {
                _isProcessingBatch = false;
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Called from background thread when a node's size is calculated
    /// </summary>
    private void OnNodeSizeCalculated(FileSystemNode node)
    {
        // Queue this node for update
        lock (_pendingUpdatesLock)
        {
            _pendingNodeUpdates.Add(node);
        }
        
        // Check if we should flush updates to UI
        if (_lastUiUpdate.ElapsedMilliseconds >= UiUpdateIntervalMs)
        {
            FlushPendingUpdates();
        }
    }
    
    /// <summary>
    /// Drain all remaining pending node additions (call at end of scan)
    /// </summary>
    private async Task DrainPendingNodeAdditionsAsync()
    {
        while (!_pendingNodeAdditions.IsEmpty)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Process all remaining additions
                while (_pendingNodeAdditions.TryDequeue(out var item))
                {
                    item.Parent.Children.Add(item.Child);
                }
            });
        }
    }

    /// <summary>
    /// Flush all pending node updates to UI thread
    /// </summary>
    private void FlushPendingUpdates()
    {
        List<FileSystemNode> nodesToUpdate;
        lock (_pendingUpdatesLock)
        {
            if (_pendingNodeUpdates.Count == 0) return;
            nodesToUpdate = _pendingNodeUpdates.ToList();
            _pendingNodeUpdates.Clear();
        }

        Dispatcher.UIThread.Post(() =>
        {
            _lastUiUpdate.Restart();
            
            foreach (var node in nodesToUpdate)
            {
                node.NotifySizeChanged();
                
                // Root node is always 100%
                if (node.Parent == null)
                {
                    node.SizePercentage = 100;
                }
            }
            
            // Only update ancestors and sorting for the last batch
            // This significantly reduces UI work
            var uniqueParents = nodesToUpdate
                .Where(n => n.Parent != null)
                .Select(n => n.Parent!)
                .Distinct()
                .ToList();

            foreach (var parent in uniqueParents)
            {
                // Re-sort children
                var sorted = parent.Children.OrderByDescending(c => c.Size).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    var node = sorted[i];
                    var currentIndex = parent.Children.IndexOf(node);
                    if (currentIndex != i)
                    {
                        parent.Children.Move(currentIndex, i);
                    }
                    node.RecalculatePercentage();
                }
            }
            
            // Update root size
            if (RootNode != null)
            {
                UpdateAncestorSizes(RootNode.Children.FirstOrDefault() ?? RootNode);
                RootNode.SizePercentage = 100;
            }
            
            // Update largest folder
            if (RootNode != null)
            {
                var largest = RootNode.Children
                    .Where(c => c.IsDirectory)
                    .OrderByDescending(c => c.Size)
                    .FirstOrDefault();
                    
                if (largest != null && (LargestFolder == null || largest.Size > LargestFolder.Size))
                {
                    LargestFolder = largest;
                    OnPropertyChanged(nameof(LargestFolderName));
                    OnPropertyChanged(nameof(LargestFolderSize));
                    OnPropertyChanged(nameof(LargestFolderPercentage));
                }
            }
            
            // Update overview
            NotifyOverviewChanged();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Update all ancestor sizes by summing their children's current sizes
    /// </summary>
    private static void UpdateAncestorSizes(FileSystemNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            // Sum up all children's sizes and file counts
            long totalSize = 0;
            int totalFiles = 0;
            int totalFolders = 0;
            
            // Take snapshot to avoid collection modified during enumeration
            foreach (var child in current.Children.ToList())
            {
                totalSize += child.Size;
                totalFiles += child.FileCount;
                totalFolders += child.IsDirectory ? 1 + child.FolderCount : 0;
            }
            
            current.Size = totalSize;
            current.FileCount = totalFiles;
            current.FolderCount = totalFolders;
            current.NotifySizeChanged();
            
            // Root is always 100%
            if (current.Parent == null)
            {
                current.SizePercentage = 100;
            }
            
            current = current.Parent;
        }
    }

    /// <summary>
    /// Insert a node into the collection maintaining descending size order
    /// </summary>
    private static void InsertSorted(ObservableCollection<FileSystemNode> children, FileSystemNode node)
    {
        // Find the correct position (largest first)
        int insertIndex = 0;
        for (int i = 0; i < children.Count; i++)
        {
            if (node.Size >= children[i].Size)
            {
                insertIndex = i;
                break;
            }
            insertIndex = i + 1;
        }
        children.Insert(insertIndex, node);
    }

    /// <summary>
    /// Re-sort a node within its parent's children after size update
    /// </summary>
    private static void ResortNodeInParent(FileSystemNode node)
    {
        var parent = node.Parent;
        if (parent == null) return;

        var children = parent.Children;
        int currentIndex = children.IndexOf(node);
        if (currentIndex < 0) return;

        // Find the correct position based on new size
        int newIndex = 0;
        for (int i = 0; i < children.Count; i++)
        {
            if (i == currentIndex) continue; // Skip self
            if (node.Size >= children[i].Size)
            {
                newIndex = i;
                break;
            }
            newIndex = i + 1;
        }

        // Adjust index if moving from earlier position
        if (currentIndex < newIndex)
        {
            newIndex--;
        }

        // Move if position changed
        if (currentIndex != newIndex)
        {
            children.Move(currentIndex, newIndex);
        }
    }

    /// <summary>
    /// Final pass to ensure all nodes are properly sorted and have correct percentages.
    /// Only processes visible (expanded) nodes to avoid slowness with millions of items.
    /// </summary>
    private static void FinalizeTreeSorting(FileSystemNode node, int depth = 0)
    {
        // Limit depth to avoid processing millions of nodes
        const int maxProcessingDepth = 10;
        if (depth > maxProcessingDepth) return;
        
        // Take a snapshot to avoid collection modified during enumeration
        var childrenSnapshot = node.Children.ToList();
        
        // Sort children by size
        var sorted = childrenSnapshot.OrderByDescending(c => c.Size).ToList();
        
        // Clear and re-add in sorted order (safer than Move during enumeration)
        node.Children.Clear();
        foreach (var child in sorted)
        {
            node.Children.Add(child);
            
            // Update percentage relative to parent
            child.RecalculatePercentage();
            child.NotifySizeChanged();
        }
        
        // Recurse into expanded directories (visible to user)
        foreach (var child in sorted)
        {
            if (child.IsDirectory && child.Children.Count > 0 && child.IsExpanded)
            {
                FinalizeTreeSorting(child, depth + 1);
            }
        }
    }

    private void NotifyOverviewChanged()
    {
        OnPropertyChanged(nameof(TotalSizeFormatted));
        OnPropertyChanged(nameof(TotalFilesFormatted));
        OnPropertyChanged(nameof(TotalFoldersFormatted));
        OnPropertyChanged(nameof(LargestFolderName));
        OnPropertyChanged(nameof(LargestFolderSize));
        OnPropertyChanged(nameof(LargestFolderPercentage));
    }
}
