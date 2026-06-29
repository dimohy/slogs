using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace Slogs.Obsidian.Drive;

internal static class SlogsObsidianDriveInstaller
{
    public const string ProductName = "Slogs Obsidian Drive";
    public const string PackageIdentifier = "Dimohy.SlogsObsidianDrive";
    public const string CommandName = "SlogsObsidianDrive";
    public const string WinFspInstallerUrl = "https://github.com/winfsp/winfsp/releases/download/v2.2B1/winfsp-2.2.26112.msi";

    private const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SlogsObsidianDrive";
    private const int HwndBroadcast = 0xffff;
    private const int WmSettingChange = 0x001a;
    private const int SmtoAbortIfHung = 0x0002;

    public static string ProductVersion
        => typeof(SlogsObsidianDriveInstaller).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+', 2)[0]
        ?? typeof(SlogsObsidianDriveInstaller).Assembly.GetName().Version?.ToString(3)
        ?? "0.1.0";

    public static bool IsInstallerCommand(IReadOnlyList<string> args)
        => args.Any(arg => arg is "--install" or "--uninstall");

    public static async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        var parsed = ParseInstallerArgs(args);
        if (parsed.Install == parsed.Uninstall)
        {
            throw new ArgumentException("Specify exactly one of --install or --uninstall.");
        }

        if (parsed.Install)
        {
            await InstallAsync(parsed, cancellationToken);
            return 0;
        }

        await UninstallAsync(parsed);
        return 0;
    }

    private static async Task InstallAsync(InstallerOptions options, CancellationToken cancellationToken)
    {
        EnsureWindows();
        var installDir = ResolveInstallDir(options.InstallDir);
        var targetExe = Path.Combine(installDir, $"{CommandName}.exe");
        var currentExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the current executable path.");

        await EnsureWinFspInstalledAsync(options.Silent, cancellationToken);

        Directory.CreateDirectory(installDir);
        if (!PathsEqual(currentExe, targetExe))
        {
            File.Copy(currentExe, targetExe, overwrite: true);
        }

        WriteUninstallRegistration(installDir, targetExe);
        EnsureUserPathContains(installDir);

        if (!options.Silent)
        {
            Console.WriteLine($"{ProductName} {ProductVersion} installed to {installDir}.");
            Console.WriteLine("Open a new terminal and run SlogsObsidianDrive --help.");
        }
    }

    private static async Task EnsureWinFspInstalledAsync(bool silent, CancellationToken cancellationToken)
    {
        if (WinFspInstallation.IsInstalled())
        {
            return;
        }

        Console.WriteLine("WinFsp is not installed. Downloading WinFsp 2.2.26112.");
        var downloadDir = Path.Combine(Path.GetTempPath(), "SlogsObsidianDrive");
        Directory.CreateDirectory(downloadDir);
        var msiPath = Path.Combine(downloadDir, "winfsp-2.2.26112.msi");

        using (var httpClient = new HttpClient())
        using (var response = await httpClient.GetAsync(WinFspInstallerUrl, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(msiPath);
            await source.CopyToAsync(target, cancellationToken);
        }

        var exitCode = await RunProcessAsync(
            "msiexec.exe",
            $"/i {Quote(msiPath)} /norestart {(silent ? "/qn" : "/passive")}",
            elevate: !IsElevated(),
            cancellationToken);
        if (exitCode is not 0 and not 3010)
        {
            throw new InvalidOperationException($"WinFsp installer failed with exit code {exitCode}.");
        }

        if (!WinFspInstallation.IsInstalled())
        {
            throw new InvalidOperationException("WinFsp installation completed, but WinFsp was not detected.");
        }
    }

    private static async Task UninstallAsync(InstallerOptions options)
    {
        EnsureWindows();
        var installDir = ResolveInstallDir(options.InstallDir);
        RemoveUserPathEntry(installDir);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryPath, throwOnMissingSubKey: false);

        if (Directory.Exists(installDir))
        {
            StartDeferredInstallDirectoryRemoval(installDir);
        }

        if (!options.Silent)
        {
            Console.WriteLine($"{ProductName} has been uninstalled.");
        }

        await Task.CompletedTask;
    }

    private static InstallerOptions ParseInstallerArgs(IReadOnlyList<string> args)
    {
        var options = new InstallerOptions();
        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--install":
                    options.Install = true;
                    break;
                case "--uninstall":
                    options.Uninstall = true;
                    break;
                case "--silent":
                    options.Silent = true;
                    break;
                case "--install-dir":
                    if (index + 1 >= args.Count)
                    {
                        throw new ArgumentException("Missing value for --install-dir.");
                    }

                    options.InstallDir = args[++index];
                    break;
                default:
                    throw new ArgumentException($"Unexpected installer argument: {arg}");
            }
        }

        return options;
    }

    private static string ResolveInstallDir(string? installDir)
    {
        if (!string.IsNullOrWhiteSpace(installDir))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(installDir));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("%LOCALAPPDATA% is not available.");
        }

        return Path.Combine(localAppData, "Programs", "SlogsObsidianDrive");
    }

    private static void WriteUninstallRegistration(string installDir, string targetExe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistryPath, writable: true)
            ?? throw new InvalidOperationException("Cannot write uninstall registration.");
        key.SetValue("DisplayName", ProductName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", ProductVersion, RegistryValueKind.String);
        key.SetValue("Publisher", "dimohy", RegistryValueKind.String);
        key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
        key.SetValue("DisplayIcon", targetExe, RegistryValueKind.String);
        key.SetValue("UninstallString", $"{Quote(targetExe)} --uninstall --install-dir {Quote(installDir)}", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"{Quote(targetExe)} --uninstall --silent --install-dir {Quote(installDir)}", RegistryValueKind.String);
        key.SetValue("URLInfoAbout", "https://github.com/dimohy/slogs", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.UtcNow.ToString("yyyyMMdd"), RegistryValueKind.String);

        if (File.Exists(targetExe))
        {
            var sizeKb = Math.Max(1, new FileInfo(targetExe).Length / 1024);
            key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, sizeKb), RegistryValueKind.DWord);
        }
    }

    private static void EnsureUserPathContains(string installDir)
    {
        using var envKey = Registry.CurrentUser.CreateSubKey("Environment", writable: true)
            ?? throw new InvalidOperationException("Cannot open the user environment registry key.");
        var currentPath = envKey.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames) as string
            ?? string.Empty;
        var entries = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Any(entry => PathsEqual(entry, installDir)))
        {
            return;
        }

        var updatedPath = string.IsNullOrWhiteSpace(currentPath)
            ? installDir
            : $"{currentPath.TrimEnd(';')};{installDir}";
        envKey.SetValue("Path", updatedPath, RegistryValueKind.ExpandString);
        BroadcastEnvironmentChanged();
    }

    private static void RemoveUserPathEntry(string installDir)
    {
        using var envKey = Registry.CurrentUser.OpenSubKey("Environment", writable: true);
        if (envKey is null)
        {
            return;
        }

        var currentPath = envKey.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames) as string
            ?? string.Empty;
        var entries = currentPath
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => !PathsEqual(entry, installDir))
            .ToArray();
        var updatedPath = string.Join(';', entries);
        envKey.SetValue("Path", updatedPath, RegistryValueKind.ExpandString);
        BroadcastEnvironmentChanged();
    }

    private static void StartDeferredInstallDirectoryRemoval(string installDir)
    {
        var commandPath = Path.Combine(Path.GetTempPath(), $"SlogsObsidianDrive-uninstall-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(
            commandPath,
            $"""
            @echo off
            ping 127.0.0.1 -n 2 > nul
            rmdir /s /q "{installDir}"
            """);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c {Quote(commandPath)}")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    private static async Task<int> RunProcessAsync(
        string fileName,
        string arguments,
        bool elevate,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = elevate,
            Verb = elevate ? "runas" : string.Empty
        }) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void EnsureWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new InvalidOperationException($"{ProductName} can only be installed on Windows.");
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string Quote(string value)
        => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static void BroadcastEnvironmentChanged()
    {
        _ = SendMessageTimeout(
            HwndBroadcast,
            WmSettingChange,
            UIntPtr.Zero,
            "Environment",
            SmtoAbortIfHung,
            5000,
            out _);
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        int hWnd,
        int msg,
        UIntPtr wParam,
        string lParam,
        int fuFlags,
        int uTimeout,
        out UIntPtr lpdwResult);

    private sealed class InstallerOptions
    {
        public bool Install { get; set; }

        public bool Uninstall { get; set; }

        public bool Silent { get; set; }

        public string? InstallDir { get; set; }
    }
}
