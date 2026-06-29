using System.Text.RegularExpressions;

namespace Slogs.Obsidian.Drive;

internal sealed record DriveOptions(
    Uri ServerUrl,
    string Token,
    string VaultName,
    string MountPoint,
    string CacheRoot,
    int PollSeconds,
    bool SyncAttachments,
    bool SyncSettings)
{
    public const string TokenEnvironmentVariable = "SLOGS_OBSIDIAN_TOKEN";

    public static string HelpText =>
        """
        Mount a Slogs Obsidian remote vault as a WinFsp-backed Windows drive.

        Install:
          Before the community winget source entry is merged:
            winget install --manifest <downloaded Dimohy.SlogsObsidianDrive manifest folder>
          After merge:
            winget install --id Dimohy.SlogsObsidianDrive --exact

        Required:
          --vault <name>       Slogs remote vault name.
          --mount <X:|path>    Drive letter or NTFS directory mount point.
          --token <token>      Slogs Bearer token with obsidian.sync scope.
                               May be provided by SLOGS_OBSIDIAN_TOKEN.

        Optional:
          --server <url>       Slogs server URL. Default: https://slogs.dev
          --cache <path>       Local cache root. Default: %LOCALAPPDATA%\Slogs\ObsidianDrive\<vault>
          --poll-seconds <n>   Remote change polling interval. Default: 30. Use 0 to disable.
          --sync-attachments <true|false>
                               Sync non-Markdown files through the attachments scope. Default: false.
          --sync-settings <true|false>
                               Sync .obsidian settings through the settings scope. Default: false.

        Example:
          SlogsObsidianDrive --vault "My Vault" --mount S:
        """;

    public static DriveOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Count; index++)
        {
            var name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument: {name}");
            }

            var key = name[2..];
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Empty option name.");
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Missing value for --{key}.");
            }

            values[key] = args[++index];
        }

        var server = GetValue(values, "server")
            ?? Environment.GetEnvironmentVariable("SLOGS_OBSIDIAN_SERVER")
            ?? "https://slogs.dev";
        if (!Uri.TryCreate(EnsureTrailingSlash(server.Trim()), UriKind.Absolute, out var serverUri)
            || serverUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("--server must be an absolute http or https URL.");
        }

        var vaultName = GetValue(values, "vault")
            ?? Environment.GetEnvironmentVariable("SLOGS_OBSIDIAN_VAULT")
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(vaultName))
        {
            throw new ArgumentException("--vault is required.");
        }

        var mountPoint = GetValue(values, "mount")
            ?? Environment.GetEnvironmentVariable("SLOGS_OBSIDIAN_MOUNT")
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            throw new ArgumentException("--mount is required.");
        }

        var token = GetValue(values, "token")
            ?? Environment.GetEnvironmentVariable(TokenEnvironmentVariable)
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException($"--token or {TokenEnvironmentVariable} is required.");
        }

        var pollSecondsText = GetValue(values, "poll-seconds") ?? "30";
        if (!int.TryParse(pollSecondsText, out var pollSeconds) || pollSeconds < 0)
        {
            throw new ArgumentException("--poll-seconds must be a non-negative integer.");
        }

        var cacheRoot = GetValue(values, "cache")
            ?? Environment.GetEnvironmentVariable("SLOGS_OBSIDIAN_CACHE")
            ?? BuildDefaultCacheRoot(vaultName);
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            throw new ArgumentException("--cache must not be empty.");
        }

        var syncAttachments = ParseBool(GetValue(values, "sync-attachments")
            ?? Environment.GetEnvironmentVariable("SLOGS_OBSIDIAN_SYNC_ATTACHMENTS")
            ?? "false", "--sync-attachments");
        var syncSettings = ParseBool(GetValue(values, "sync-settings")
            ?? Environment.GetEnvironmentVariable("SLOGS_OBSIDIAN_SYNC_SETTINGS")
            ?? "false", "--sync-settings");

        return new DriveOptions(
            serverUri,
            token.Trim(),
            vaultName.Trim(),
            mountPoint.Trim(),
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(cacheRoot.Trim())),
            pollSeconds,
            syncAttachments,
            syncSettings);
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;

    private static string EnsureTrailingSlash(string value)
        => value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";

    private static bool ParseBool(string value, string optionName)
    {
        if (bool.TryParse(value.Trim(), out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"{optionName} must be true or false.");
    }

    private static string BuildDefaultCacheRoot(string vaultName)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new ArgumentException("%LOCALAPPDATA% is not available. Pass --cache explicitly.");
        }

        return Path.Combine(localAppData, "Slogs", "ObsidianDrive", SanitizeCacheName(vaultName));
    }

    private static string SanitizeCacheName(string value)
    {
        var sanitized = Regex.Replace(value.Trim(), @"[^A-Za-z0-9._-]+", "-");
        return string.IsNullOrWhiteSpace(sanitized) ? "vault" : sanitized.Trim('-');
    }
}
