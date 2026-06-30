using Fsp;

namespace Slogs.Obsidian.Drive;

internal static class SlogsObsidianDriveApplication
{
    public static async Task RunAsync(DriveOptions options)
    {
        WinFspInstallation.RequireInstalled();

        Directory.CreateDirectory(options.CacheRoot);
        var filesRoot = Path.Combine(options.CacheRoot, "files");
        Directory.CreateDirectory(filesRoot);

        using var httpClient = new HttpClient { BaseAddress = options.ServerUrl };
        var remoteClient = new SlogsObsidianRemoteClient(httpClient, options.Token);
        var stateStore = new SlogsObsidianDriveStateStore(Path.Combine(options.CacheRoot, "state.json"));
        var syncService = new SlogsObsidianSyncService(
            remoteClient,
            stateStore,
            filesRoot,
            options.VaultName,
            options.SyncAttachments,
            options.SyncSettings);

        Console.WriteLine($"Preparing Slogs Obsidian vault '{options.VaultName}' from {options.ServerUrl}.");
        await syncService.InitializeAsync();

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var pollTask = options.PollSeconds == 0
            ? Task.CompletedTask
            : syncService.RunRemotePollingAsync(TimeSpan.FromSeconds(options.PollSeconds), cancellation.Token);

        using var host = CreateHost(filesRoot, syncService);
        var preflightStatus = host.Preflight(options.MountPoint);
        if (preflightStatus < 0)
        {
            throw new InvalidOperationException($"WinFsp preflight failed for '{options.MountPoint}'. NTSTATUS: 0x{preflightStatus:x8}");
        }

        var mountStatus = host.Mount(options.MountPoint);
        if (mountStatus < 0)
        {
            throw new InvalidOperationException($"WinFsp could not mount '{options.MountPoint}'. NTSTATUS: 0x{mountStatus:x8}");
        }

        Console.WriteLine($"Mounted Slogs Obsidian vault '{options.VaultName}' at {host.MountPoint()}.");
        Console.WriteLine("Open this drive or mount directory in Obsidian. Press Ctrl+C to unmount.");

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            host.Unmount();
            cancellation.Cancel();
            await pollTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await syncService.PushLocalChangesAsync();
            Console.WriteLine("Unmounted Slogs Obsidian drive.");
        }
    }

    private static FileSystemHost CreateHost(string filesRoot, SlogsObsidianSyncService syncService)
    {
        var fileSystem = new SlogsObsidianFileSystem(filesRoot, syncService);
        var host = new FileSystemHost(fileSystem)
        {
            FileSystemName = "SlogsObsidian",
            CaseSensitiveSearch = false,
            CasePreservedNames = true,
            UnicodeOnDisk = true,
            PersistentAcls = false,
            SectorSize = 4096,
            SectorsPerAllocationUnit = 1,
            MaxComponentLength = 255,
            FileInfoTimeout = 1000,
            VolumeInfoTimeout = 1000,
            DirInfoTimeout = 1000,
            PostCleanupWhenModifiedOnly = true,
            FlushAndPurgeOnCleanup = true
        };

        return host;
    }
}
