using System.Text.Json;
using Slogs.Data;

namespace Slogs.Obsidian.Drive;

internal sealed class SlogsObsidianDriveState
{
    public string VaultName { get; set; } = string.Empty;

    public Guid VaultId { get; set; }

    public long LastRemoteVersion { get; set; }

    public Dictionary<string, SlogsObsidianDriveFileState> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SlogsObsidianDriveFileState
{
    public long Version { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    public bool Deleted { get; set; }

    public string Scope { get; set; } = ObsidianSyncScopes.Markdown;

    public string Kind { get; set; } = ObsidianVaultFileKinds.Markdown;
}

internal sealed class SlogsObsidianDriveStateStore(string statePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<SlogsObsidianDriveState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(statePath))
        {
            return new SlogsObsidianDriveState();
        }

        await using var stream = File.OpenRead(statePath);
        var state = await JsonSerializer.DeserializeAsync<SlogsObsidianDriveState>(stream, JsonOptions, cancellationToken)
            ?? new SlogsObsidianDriveState();
        state.Files = new Dictionary<string, SlogsObsidianDriveFileState>(
            state.Files,
            StringComparer.OrdinalIgnoreCase);
        return state;
    }

    public async Task SaveAsync(SlogsObsidianDriveState state, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)
            ?? throw new InvalidOperationException("State path has no parent directory."));
        await using var stream = File.Create(statePath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
    }
}
