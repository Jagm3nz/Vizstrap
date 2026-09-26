using System.Diagnostics;
using Microsoft.Win32;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Platform;
using Vizstrap.Core.Roblox;
using Vizstrap.Core.Storage;

namespace Vizstrap.Core.Install;

public enum UpdateStage
{
    Connecting,
    CheckingVersion,
    Downloading,
    Finalizing,
}

public sealed record UpdateProgress(
    UpdateStage Stage,
    bool IsFreshInstall = false,
    long BytesDone = 0,
    long BytesTotal = 0,
    int PackagesDone = 0,
    int PackagesTotal = 0,
    double BytesPerSecond = 0);

public sealed record InstalledRoblox(string VersionGuid, string Directory, string ExecutablePath);

/// <summary>A version downloaded and extracted next to the installed one, not switched to yet.</summary>
public sealed record PreparedVersion(string VersionGuid, string Directory, string ExecutablePath, IReadOnlyList<Package> Packages);

/// <param name="Deferred">The newer version left for a background update, if the update was put off.</param>
public sealed record UpdateResult(InstalledRoblox Installed, ClientVersion? Deferred = null);

public sealed class NotEnoughDiskSpaceException(long requiredBytes, long availableBytes)
    : Exception($"Not enough disk space: {requiredBytes} bytes required, {availableBytes} available.")
{
    public long RequiredBytes { get; } = requiredBytes;

    public long AvailableBytes { get; } = availableBytes;
}

/// <summary>
/// Makes sure the latest Roblox Player is installed. State.json is only updated after every package is
/// downloaded, verified and extracted, so a failed or cancelled update never breaks the current install.
/// </summary>
public sealed class RobloxUpdater
{
    public const string PlayerExecutableName = "RobloxPlayerBeta.exe";

    private const string LogSource = nameof(RobloxUpdater);

    // byte-for-byte what the official bootstrapper writes
    private const string AppSettingsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<Settings>\n" +
        "\t<ContentFolder>content</ContentFolder>\n" +
        "\t<BaseUrl>http://www.roblox.com</BaseUrl>\n" +
        "</Settings>\n";

    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    private readonly VizstrapPaths _paths;
    private readonly JsonStore<State> _state;
    private readonly RobloxDeployment _deployment;
    private readonly PackageDownloader _downloader;
    private readonly Func<string, long> _freeSpace;
    private readonly RegistryKey _currentUser;
    private readonly Func<string, bool> _isInUse;

    /// <param name="isInUse">Whether Roblox is running from a version folder, which then is kept.</param>
    public RobloxUpdater(
        VizstrapPaths paths,
        JsonStore<State> state,
        RobloxDeployment deployment,
        PackageDownloader downloader,
        Func<string, long>? freeSpace = null,
        RegistryKey? currentUser = null,
        Func<string, bool>? isInUse = null)
    {
        _paths = paths;
        _state = state;
        _deployment = deployment;
        _downloader = downloader;
        _freeSpace = freeSpace ?? FreeSpace;
        _currentUser = currentUser ?? Registry.CurrentUser;
        _isInUse = isInUse ?? (directory => ProcessPaths.FindRunningUnder("RobloxPlayerBeta", directory).Count > 0);
    }

    public static long FreeSpace(string path) => new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace;

    /// <summary>The currently installed version, or null when there is none or its executable is missing.</summary>
    public InstalledRoblox? GetInstalled()
    {
        string? version = _state.Value.PlayerVersion;

        if (string.IsNullOrEmpty(version))
            return null;

        string directory = _paths.VersionDirectory(version);
        string executable = Path.Combine(directory, PlayerExecutableName);

        return File.Exists(executable) ? new InstalledRoblox(version, directory, executable) : null;
    }

    /// <summary>Roblox's newest version, choosing a download mirror on the way (as a background update starts).</summary>
    public async Task<ClientVersion> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        await _deployment.SelectMirrorAsync(cancellationToken);
        return await _deployment.GetLatestVersionAsync(cancellationToken);
    }

    public async Task<InstalledRoblox> EnsureLatestAsync(IProgress<UpdateProgress>? progress, CancellationToken cancellationToken) =>
        (await EnsureLatestAsync(null, progress, cancellationToken)).Installed;

    /// <summary>
    /// Like the overload without <paramref name="deferUpdate"/>, but when that says so, returns the
    /// installed Roblox right away and leaves the new version for a background update
    /// (<see cref="PrepareAsync"/> and <see cref="Commit"/>).
    /// </summary>
    public async Task<UpdateResult> EnsureLatestAsync(
        Func<InstalledRoblox, ClientVersion, CancellationToken, Task<bool>>? deferUpdate,
        IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
    {
        // another Vizstrap instance may have updated while we waited for the lock
        _state.Load();

        var installed = GetInstalled();

        ClientVersion latest;

        try
        {
            progress?.Report(new UpdateProgress(UpdateStage.Connecting));
            await _deployment.SelectMirrorAsync(cancellationToken);

            progress?.Report(new UpdateProgress(UpdateStage.CheckingVersion));
            latest = await _deployment.GetLatestVersionAsync(cancellationToken);
        }
        catch (RobloxConnectionException ex) when (installed is not null)
        {
            Log.Warn(LogSource, $"Offline ({ex.InnerException?.Message}), launching installed {installed.VersionGuid}");
            return new UpdateResult(installed);
        }

        if (installed is not null && installed.VersionGuid == latest.VersionGuid && !_state.Value.ForceReinstall)
        {
            Log.Info(LogSource, $"{installed.VersionGuid} is up to date");
            CleanupOldVersions(installed.VersionGuid);
            return new UpdateResult(installed);
        }

        if (installed is not null && deferUpdate is not null && await deferUpdate(installed, latest, cancellationToken))
        {
            Log.Info(LogSource, $"Launching {installed.VersionGuid} now; {latest.VersionGuid} ({latest.Version}) comes in the background");
            return new UpdateResult(installed, latest);
        }

        Log.Info(LogSource, installed is null
            ? $"Installing {latest.VersionGuid}"
            : $"Updating {installed.VersionGuid} -> {latest.VersionGuid} (forced: {_state.Value.ForceReinstall})");

        var prepared = await PrepareAsync(latest.VersionGuid, isFreshInstall: installed is null, progress, cancellationToken);
        return new UpdateResult(Commit(prepared));
    }

    /// <summary>
    /// Downloads and extracts a version into its own folder, leaving the installed one and State.json
    /// alone, so Roblox can keep running from the old version meanwhile.
    /// </summary>
    public async Task<PreparedVersion> PrepareAsync(
        string versionGuid, bool isFreshInstall, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
    {
        var packages = SelectPackages(await _deployment.GetPackageManifestAsync(versionGuid, cancellationToken));

        EnsureDiskSpace(packages);

        string versionDirectory = _paths.VersionDirectory(versionGuid);

        // leftovers from an interrupted install must not mix with the fresh files
        TryDeleteDirectory(versionDirectory);
        Directory.CreateDirectory(versionDirectory);

        long bytesTotal = packages.Sum(package => package.PackedSize);
        long bytesDone = 0;
        int packagesDone = 0;
        var meter = new SpeedMeter();
        var sinceReport = Stopwatch.StartNew();

        void Report(bool force)
        {
            if (progress is null || (!force && sinceReport.Elapsed < ReportInterval))
                return;

            sinceReport.Restart();
            progress.Report(new UpdateProgress(UpdateStage.Downloading, isFreshInstall,
                bytesDone, bytesTotal, packagesDone, packages.Count, meter.BytesPerSecond));
        }

        Report(force: true);

        var extractions = new List<Task>();

        try
        {
            foreach (var package in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long before = bytesDone;
                string url = _deployment.GetFileUrl(versionGuid, package.Name);

                var download = await _downloader.DownloadAsync(package, url, bytes =>
                {
                    bytesDone += bytes;
                    meter.Add(bytes);
                    Report(force: false);
                }, cancellationToken);

                // cache hits report nothing, so settle on the exact package size either way
                bytesDone = before + package.PackedSize;
                packagesDone++;
                Report(force: true);

                string target = Path.Combine(versionDirectory, PackageMap.Player[package.Name]);
                extractions.Add(Task.Run(() => PackageExtractor.Extract(download.Path, target, cancellationToken), cancellationToken));
            }

            progress?.Report(new UpdateProgress(UpdateStage.Finalizing, isFreshInstall));
            await Task.WhenAll(extractions);
        }
        catch
        {
            // never leave extraction running behind our back
            try
            {
                await Task.WhenAll(extractions);
            }
            catch
            {
                // the original failure is the one worth reporting
            }

            throw;
        }

        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "AppSettings.xml"), AppSettingsXml, cancellationToken);

        string executable = Path.Combine(versionDirectory, PlayerExecutableName);

        if (!File.Exists(executable))
            throw new InvalidDataException($"{PlayerExecutableName} is missing after installing {versionGuid}.");

        return new PreparedVersion(versionGuid, versionDirectory, executable, packages);
    }

    /// <summary>
    /// Switches to a prepared version: State.json, Windows compatibility settings, then cleanup of old
    /// versions (except one Roblox still runs from) and of cached packages no longer needed.
    /// </summary>
    public InstalledRoblox Commit(PreparedVersion prepared)
    {
        // a background update commits long after it started; take the state as it is now
        _state.Load();
        var previous = GetInstalled();

        var state = _state.Value;
        state.PlayerVersion = prepared.VersionGuid;
        state.PackageHashes = prepared.Packages.ToDictionary(package => package.Name, package => package.Signature);
        state.PlayerSizeKb = prepared.Packages.Sum(package => package.PackedSize + package.Size) / 1024;
        state.ForceReinstall = false;
        _state.Save();

        Log.Info(LogSource, $"Installed {prepared.VersionGuid}");

        if (previous is not null && previous.VersionGuid != prepared.VersionGuid)
            CompatibilityFlags.Migrate(previous.ExecutablePath, prepared.ExecutablePath, _currentUser);

        CleanupOldVersions(prepared.VersionGuid);
        CleanupDownloadCache(state.PackageHashes.Values);

        return new InstalledRoblox(prepared.VersionGuid, prepared.Directory, prepared.ExecutablePath);
    }

    internal static List<Package> SelectPackages(IEnumerable<Package> manifest)
    {
        var selected = new List<Package>();

        foreach (var package in manifest)
        {
            // the manifest also lists RobloxPlayerInstaller.exe, which is the official bootstrapper itself
            if (!package.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || PackageMap.Ignored.Contains(package.Name))
                continue;

            if (!PackageMap.Player.ContainsKey(package.Name))
            {
                Log.Warn(LogSource, $"Unknown package {package.Name} skipped; PackageMap needs an entry for it");
                continue;
            }

            selected.Add(package);
        }

        return selected;
    }

    private void EnsureDiskSpace(IReadOnlyCollection<Package> packages)
    {
        long required = packages.Sum(package => package.Size) +
            packages.Where(package => !File.Exists(Path.Combine(_paths.Downloads, package.Signature))).Sum(package => package.PackedSize);

        Directory.CreateDirectory(_paths.Base);
        long available = _freeSpace(_paths.Base);

        if (available < required)
            throw new NotEnoughDiskSpaceException(required, available);
    }

    private void CleanupOldVersions(string keepVersion)
    {
        if (!Directory.Exists(_paths.Versions))
            return;

        foreach (string directory in Directory.GetDirectories(_paths.Versions))
        {
            if (string.Equals(Path.GetFileName(directory), keepVersion, StringComparison.OrdinalIgnoreCase))
                continue;

            // deleting under a running Roblox would pull files it still loads; the next launch retries
            if (_isInUse(directory))
            {
                Log.Info(LogSource, $"Keeping {Path.GetFileName(directory)} for now: Roblox is running from it");
                continue;
            }

            TryDeleteDirectory(directory);
        }
    }

    private void CleanupDownloadCache(IEnumerable<string> keepSignatures)
    {
        if (!Directory.Exists(_paths.Downloads))
            return;

        var keep = keepSignatures.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.GetFiles(_paths.Downloads))
        {
            if (keep.Contains(Path.GetFileName(file)))
                continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn(LogSource, $"Could not delete cached {Path.GetFileName(file)}: {ex.Message}");
            }
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // usually Roblox still running from it; retried on the next launch
            Log.Warn(LogSource, $"Could not delete {directory}: {ex.Message}");
        }
    }
}
