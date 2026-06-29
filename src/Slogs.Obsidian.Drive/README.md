# Slogs Obsidian Drive

Windows-only WinFsp integration that mounts a Slogs Obsidian remote vault as a local filesystem. Obsidian can open the mounted drive or mount directory directly without the Obsidian plugin.

## Requirements

- Windows.
- `winget`.
- A Slogs Bearer token with the `obsidian.sync` scope.

The installer checks WinFsp and downloads the official WinFsp `2.2.26112` MSI from GitHub if it is missing.

## Install

The winget community source entry is under review. Until it is merged, install from the GitHub Release through the checked-in winget manifest:

```powershell
$manifest = Join-Path $env:TEMP "SlogsObsidianDrive-winget"
Remove-Item $manifest -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $manifest | Out-Null
$base = "https://raw.githubusercontent.com/dimohy/slogs/main/packaging/winget/manifests/d/Dimohy/SlogsObsidianDrive/0.1.0"
@("Dimohy.SlogsObsidianDrive.yaml","Dimohy.SlogsObsidianDrive.locale.en-US.yaml","Dimohy.SlogsObsidianDrive.installer.yaml") | ForEach-Object {
    Invoke-WebRequest "$base/$_" -OutFile (Join-Path $manifest $_)
}
winget install --manifest $manifest --accept-package-agreements --accept-source-agreements
```

After the community manifest is merged into winget-pkgs, this shorter command is enough:

```powershell
winget install --id Dimohy.SlogsObsidianDrive --exact
```

The GitHub Release asset can also install itself:

```powershell
.\SlogsObsidianDrive-0.1.0-win-x64.exe --install
```

## Mount

```powershell
$env:SLOGS_OBSIDIAN_TOKEN = "<obsidian.sync token>"
SlogsObsidianDrive --vault "My Vault" --mount S:
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
- Windows drive capacity reports the account storage limit as total capacity and the active vault file size as used space.
