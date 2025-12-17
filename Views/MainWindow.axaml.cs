using Avalonia.Controls;
using Avalonia.Input;
using Disc.Analyzer.ViewModels;

namespace Disc.Analyzer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Add keyboard handler for search navigation
        SearchBox.KeyDown += OnSearchBoxKeyDown;
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        
        if (e.Key == Key.Enter)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                vm.SearchPreviousCommand.Execute(null);
            }
            else
            {
                vm.SearchNextCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Clear search
            vm.SearchText = string.Empty;
            e.Handled = true;
        }
    }
}