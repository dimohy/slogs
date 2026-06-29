using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Slogs.Obsidian.Drive;

internal static class WinFspInstallation
{
    public static void RequireInstalled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new InvalidOperationException("Slogs Obsidian Drive requires Windows and WinFsp.");
        }

        if (!FindInstalledWinFspBinaries().Any(File.Exists)
            && !IsWinFspServiceRegistered()
            && !IsWinFspRegisteredAsInstalledProduct())
        {
            throw new InvalidOperationException(
                "WinFsp is required before mounting a Slogs Obsidian drive. Install WinFsp from https://winfsp.dev/rel/ and run this command again.");
        }
    }

    private static IEnumerable<string> FindInstalledWinFspBinaries()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(root, "WinFsp", "bin", "winfsp-x64.dll");
            yield return Path.Combine(root, "WinFsp", "bin", "winfsp-x86.dll");
            yield return Path.Combine(root, "WinFsp", "bin", "winfsp-msil.dll");
        }
    }

    private static bool IsWinFspServiceRegistered()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WinFsp.Launcher");
        return key is not null;
    }

    private static bool IsWinFspRegisteredAsInstalledProduct()
        => FindUninstallDisplayNames(RegistryView.Registry64)
            .Concat(FindUninstallDisplayNames(RegistryView.Registry32))
            .Any(name => name.Contains("WinFsp", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> FindUninstallDisplayNames(RegistryView registryView)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
        using var uninstallKey = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninstallKey is null)
        {
            yield break;
        }

        foreach (var subKeyName in uninstallKey.GetSubKeyNames())
        {
            using var subKey = uninstallKey.OpenSubKey(subKeyName);
            if (subKey?.GetValue("DisplayName") is string displayName)
            {
                yield return displayName;
            }
        }
    }
}
