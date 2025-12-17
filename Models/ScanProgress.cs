namespace Disc.Analyzer.Models;

public record ScanProgress(
    int FoldersScanned,
    int FilesScanned,
    long BytesScanned,
    string CurrentPath
);
