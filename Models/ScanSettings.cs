using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Disc.Analyzer.Models;

public partial class ScanSettings : ObservableObject
{
    [ObservableProperty]
    private bool _showFiles = false;

    [ObservableProperty]
    private int _maxParallelism = Environment.ProcessorCount;
}
