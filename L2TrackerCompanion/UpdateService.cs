using Velopack;
using Velopack.Sources;

namespace L2TrackerCompanion;

/// <summary>
/// Wraps Velopack's <see cref="UpdateManager"/> against the public
/// l2-tracker-companion GitHub Releases feed. No access token: the repo is public,
/// and Velopack only ever reads release assets from it.
/// </summary>
public sealed class UpdateService
{
    private const string DefaultRepoUrl = "https://github.com/gregpopiel/l2-tracker-companion";

    private readonly UpdateManager _manager;

    public UpdateService(string repoUrl = DefaultRepoUrl)
    {
        _manager = new UpdateManager(new GithubSource(repoUrl, accessToken: null, prerelease: false));
    }

    /// <summary>
    /// The running app's version. Reads it from Velopack when installed (matching
    /// what the update feed compares against); falls back to the version compiled
    /// into the assembly (`&lt;Version&gt;` in the csproj) when not — e.g. `dotnet run`,
    /// where <see cref="UpdateManager.CurrentVersion"/> is null.
    /// </summary>
    public string CurrentVersion
        => _manager.CurrentVersion?.ToString()
            ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "unknown";

    /// <summary>
    /// Checks the GitHub Releases feed and, if a newer version exists, downloads it
    /// in the background. Returns the pending version, or null if already current,
    /// not running as a Velopack install (e.g. `dotnet run`), or the check/download
    /// failed — a failure here (offline, GitHub hiccup) is never worth surfacing;
    /// the next periodic check just tries again.
    /// </summary>
    public async Task<UpdateInfo?> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!_manager.IsInstalled)
        {
            return null;
        }

        try
        {
            var updateInfo = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updateInfo is null)
            {
                return null;
            }

            await _manager.DownloadUpdatesAsync(updateInfo, cancelToken: cancellationToken).ConfigureAwait(false);
            return updateInfo;
        }
        catch (Exception ex)
        {
            // Logged rather than rethrown: a failed check/download (offline, GitHub
            // hiccup) must never crash the app, but a real misconfiguration (wrong
            // repo, bad release channel) should still leave a trace to find, instead
            // of presenting identically to "user is offline" forever.
            System.Diagnostics.Trace.WriteLine(ex);
            return null;
        }
    }

    /// <summary>
    /// Applies a downloaded update and restarts the app. Only ever called from an
    /// explicit user click (see MainWindow) — never automatically, since the app
    /// may be mid-poll or holding an unsaved farm-log delta.
    /// </summary>
    public void ApplyAndRestart(UpdateInfo updateInfo) => _manager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
}
