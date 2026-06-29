# Slogs Obsidian Drive

Windows-only WinFsp integration that mounts a Slogs Obsidian remote vault as a local filesystem. Obsidian can open the mounted drive or mount directory directly without the Obsidian plugin.

## Requirements

- Windows.
- WinFsp 2026 `2.2.26112` or a compatible 2.2 runtime installed from `https://winfsp.dev/rel/`.
- A Slogs Bearer token with the `obsidian.sync` scope.

## Run

```powershell
$env:SLOGS_OBSIDIAN_TOKEN = "<obsidian.sync token>"
dotnet run --project src\Slogs.Obsidian.Drive -- --vault "My Vault" --mount S:
```

Optional arguments:

- `--server https://slogs.dev`
- `--cache C:\Users\<you>\AppData\Local\Slogs\ObsidianDrive\MyVault`
- `--poll-seconds 30`
- `--sync-attachments true`
- `--sync-settings true`

The token is read from `--token` or `SLOGS_OBSIDIAN_TOKEN` and is not written to logs or state files.

## Sync Semantics

- Markdown files outside `.obsidian/` are synced through `/api/obsidian/*` by default.
- Non-Markdown files sync only when `--sync-attachments true` is set.
- `.obsidian/` settings sync only when `--sync-settings true` is set.
- Remote deletes are tombstones on Slogs, so restore/version inspection remains available through the server API.
- Remote conflicts return an IO error to the calling filesystem operation and are logged without overwriting the remote file.
- The local cache state is stored in `state.json` beside the cache `files/` directory, not inside the mounted vault root.
- The drive checks Windows/WinFsp installation and runs WinFsp mount preflight before mounting.
