using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Disc.Analyzer.Models;

namespace Disc.Analyzer.Services;

public class DirectoryScanner
{
    private readonly ScanSettings _settings;
    private int _foldersScanned;
    private int _filesScanned;
    private long _bytesScanned;

    public DirectoryScanner(ScanSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Called when progress changes (folder count, file count, bytes)
    /// </summary>
    public event Action<ScanProgress>? ProgressChanged;

    /// <summary>
    /// Called when the root node is created (first event fired).
    /// This is called from background threads - use dispatcher in handler.
    /// </summary>
    public event Action<FileSystemNode>? RootNodeCreated;

    /// <summary>
    /// Called when a new node is discovered and added to the tree.
    /// The callback receives the parent node and the new child node.
    /// This is called from background threads - use dispatcher in handler.
    /// </summary>
    public event Action<FileSystemNode, FileSystemNode>? NodeDiscovered;

    /// <summary>
    /// Called when a node's size has been fully calculated.
    /// This is called from background threads - use dispatcher in handler.
    /// </summary>
    public event Action<FileSystemNode>? NodeSizeCalculated;

    public async Task<FileSystemNode?> ScanAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        // Cross-platform: .NET handles path separators automatically
        // DirectoryInfo and FileInfo abstract platform differences
        if (!Directory.Exists(rootPath))
            return null;

        _foldersScanned = 0;
        _filesScanned = 0;
        _bytesScanned = 0;

        var rootInfo = new DirectoryInfo(rootPath);
        var rootNode = new FileSystemNode
        {
            Name = rootInfo.Name,
            FullPath = rootInfo.FullName,
            IsDirectory = true,
            IsExpanded = true,  // Root is always expanded
            LastModified = rootInfo.LastWriteTime
        };

        // Notify that root node is created - add to UI immediately
        RootNodeCreated?.Invoke(rootNode);

        // Scan in parallel with real-time updates (depth 0 = root)
        await ScanDirectoryParallelAsync(rootNode, rootInfo, 0, cancellationToken);

        // Root node is always 100%
        rootNode.SizePercentage = 100;
        rootNode.NotifySizeChanged();

        // Calculate percentages after scan completes (each item relative to parent)
        CalculatePercentages(rootNode);

        // Sort all children by size (largest first)
        SortChildrenBySize(rootNode);

        return rootNode;
    }

    private async Task ScanDirectoryParallelAsync(
        FileSystemNode node,
        DirectoryInfo dirInfo,
        int depth,
        CancellationToken cancellationToken)
    {
        var subdirectories = new ConcurrentBag<(FileSystemNode Node, DirectoryInfo Info)>();
        long totalSize = 0;
        int fileCount = 0;
        int folderCount = 0;
        DateTime latestModified = node.LastModified;

        try
        {
            // Check cancellation at start of each directory
            cancellationToken.ThrowIfCancellationRequested();

            // Process files first (fast)
            foreach (var file in dirInfo.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var fileLength = file.Length;
                    totalSize += fileLength;
                    fileCount++;
                    Interlocked.Increment(ref _filesScanned);
                    Interlocked.Add(ref _bytesScanned, fileLength);

                    if (file.LastWriteTime > latestModified)
                        latestModified = file.LastWriteTime;

                    if (_settings.ShowFiles)
                    {
                        var fileNode = new FileSystemNode
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            Size = fileLength,
                            FileCount = 1,
                            IsDirectory = false,
                            LastModified = file.LastWriteTime,
                            Parent = node
                        };
                        // Notify about new file node for real-time UI update
                        NodeDiscovered?.Invoke(node, fileNode);
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }

            // Get subdirectories and create nodes immediately for real-time display
            foreach (var subDir in dirInfo.EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var subNode = new FileSystemNode
                    {
                        Name = subDir.Name,
                        FullPath = subDir.FullName,
                        IsDirectory = true,
                        IsExpanded = depth < 1,  // Only expand root + first level (depth 0)
                        LastModified = subDir.LastWriteTime,
                        Parent = node
                    };

                    // Notify about new directory node immediately for real-time UI
                    NodeDiscovered?.Invoke(node, subNode);

                    subdirectories.Add((subNode, subDir));
                    folderCount++;
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }

            Interlocked.Increment(ref _foldersScanned);

            // Report progress frequently for responsiveness
            if (_foldersScanned % 50 == 0)
            {
                ReportProgress(dirInfo.FullName);
            }

            // Parallel scan subdirectories with cancellation support
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = _settings.MaxParallelism,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(subdirectories, options, async (item, ct) =>
            {
                await ScanDirectoryParallelAsync(item.Node, item.Info, depth + 1, ct);
            });

            // Aggregate sizes from children after they've all been scanned
            foreach (var (childNode, _) in subdirectories)
            {
                totalSize += childNode.Size;
                fileCount += childNode.FileCount;
                folderCount += childNode.FolderCount;

                if (childNode.LastModified > latestModified)
                    latestModified = childNode.LastModified;
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (OperationCanceledException) { throw; } // Let cancellation bubble up
        catch (IOException) { }

        // Update node with final calculated values
        node.Size = totalSize;
        node.FileCount = fileCount;
        node.FolderCount = folderCount;
        node.LastModified = latestModified;
        node.NotifySizeChanged();

        // Notify that this node's size is now calculated (for real-time UI update)
        NodeSizeCalculated?.Invoke(node);
    }

    private void CalculatePercentages(FileSystemNode node)
    {
        // Each child's percentage is relative to its parent's size
        foreach (var child in node.Children)
        {
            child.UpdateSizePercentage(node.Size);
            if (child.IsDirectory)
            {
                CalculatePercentages(child);
            }
        }
    }

    private void SortChildrenBySize(FileSystemNode node)
    {
        var sorted = node.Children.OrderByDescending(c => c.Size).ToList();
        node.Children.Clear();
        foreach (var child in sorted)
        {
            node.Children.Add(child);
            if (child.IsDirectory && child.Children.Count > 0)
            {
                SortChildrenBySize(child);
            }
        }
    }

    private void ReportProgress(string currentPath)
    {
        ProgressChanged?.Invoke(new ScanProgress(
            _foldersScanned,
            _filesScanned,
            _bytesScanned,
            currentPath
        ));
    }

    public ScanProgress GetCurrentProgress(string currentPath = "")
    {
        return new ScanProgress(_foldersScanned, _filesScanned, _bytesScanned, currentPath);
    }
}
