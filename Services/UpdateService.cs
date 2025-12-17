using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Disc.Analyzer.Services;

public class UpdateService
{
    private readonly UpdateManager _updateManager;
    private readonly SettingsService _settingsService;
    private UpdateInfo? _pendingUpdate;

    public event Action<string>? UpdateAvailable;
    public event Action<int>? DownloadProgress;
    public event Action? UpdateReady;
    public event Action<string>? UpdateError;

    public bool HasPendingUpdate => _pendingUpdate != null;
    public string? PendingVersion => _pendingUpdate?.TargetFullRelease?.Version?.ToString();

    public UpdateService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        
        // Configure update source - replace with your actual GitHub repo or update server
        // For GitHub releases: new GithubSource("https://github.com/yourusername/DiscAnalyzer", null, false)
        // For a custom server: new SimpleWebSource("https://your-update-server.com/releases")
        var source = new GithubSource("https://github.com/johnkesko/DiscAnalyzer", null, false);
        
        _updateManager = new UpdateManager(source);
    }

    /// <summary>
    /// Check for updates in the background
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        if (!_settingsService.Settings.AutoUpdate)
            return;

        // Don't check for updates if not installed via Velopack
        if (!_updateManager.IsInstalled)
            return;

        try
        {
            _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
            
            if (_pendingUpdate != null)
            {
                var version = _pendingUpdate.TargetFullRelease?.Version?.ToString() ?? "unknown";
                UpdateAvailable?.Invoke(version);
                
                // Auto-download in background
                await DownloadUpdateAsync();
            }
        }
        catch (Exception ex)
        {
            UpdateError?.Invoke($"Failed to check for updates: {ex.Message}");
        }
    }

    /// <summary>
    /// Download the pending update
    /// </summary>
    public async Task DownloadUpdateAsync()
    {
        if (_pendingUpdate == null)
            return;

        try
        {
            await _updateManager.DownloadUpdatesAsync(
                _pendingUpdate,
                progress => DownloadProgress?.Invoke(progress));
            
            UpdateReady?.Invoke();
        }
        catch (Exception ex)
        {
            UpdateError?.Invoke($"Failed to download update: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply the update and restart the application
    /// </summary>
    public void ApplyUpdateAndRestart()
    {
        if (_pendingUpdate == null)
            return;

        try
        {
            _updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
        }
        catch (Exception ex)
        {
            UpdateError?.Invoke($"Failed to apply update: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply the update on next application exit
    /// </summary>
    public void ApplyUpdateOnExit()
    {
        if (_pendingUpdate == null)
            return;

        try
        {
            _updateManager.ApplyUpdatesAndExit(_pendingUpdate);
        }
        catch (Exception ex)
        {
            UpdateError?.Invoke($"Failed to schedule update: {ex.Message}");
        }
    }
}
