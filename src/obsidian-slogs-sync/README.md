# Slogs Sync for Obsidian

Slogs Sync synchronizes an Obsidian vault with the Slogs remote Obsidian Vault API.

## Install

### Community plugin

After Obsidian approves the community plugin submission:

1. Open Obsidian settings.
2. Go to Community plugins and turn off Restricted mode if needed.
3. Select Browse and search for `Slogs Sync`.
4. Install and enable the plugin.

### Beta install before approval

Until the community plugin review is merged, install without copying files by using the BRAT community plugin:

1. Install `BRAT` from Obsidian community plugins.
2. Run `BRAT: Add a beta plugin for testing`.
3. Enter this repository URL:

```text
https://github.com/dimohy/obsidian-slogs-sync
```

4. Enable `Slogs Sync` from Community plugins.

## Configure

1. In Slogs, create a token with the `obsidian.sync` scope.
2. In Obsidian, open Settings > Slogs Sync.
3. Set the Slogs server URL, normally `https://slogs.dev`.
4. Paste the `obsidian.sync` token.
5. Set a remote vault name, or leave it blank to use the current Obsidian vault name.

## Commands

- `Sync all Slogs files`
- `Push current file to Slogs`
- `Pull remote changes from Slogs`
- `Open mapped Slogs post`
- `Open Slogs vault settings`

## Scope

Markdown files outside `.obsidian/` sync by default. Attachments and `.obsidian` settings are explicit opt-in toggles in plugin settings.

Frontmatter-triggered mappings are also opt-in:

```yaml
---
slogs.post: true
slogs.slug: my-slogs-post
slogs.llmWiki: true
slogs.llmWiki.categoryPath: slogs/obsidian-import
---
```

Conflicts open a modal so the user can use the remote copy, keep the local copy, or skip the file instead of overwriting silently.

## Develop

```powershell
npm install
npm run check
npm run test
npm run build
```

Release assets for Obsidian are `manifest.json`, `main.js`, and `styles.css` if a future release adds styles. The GitHub release tag must match the `version` in `manifest.json`.
