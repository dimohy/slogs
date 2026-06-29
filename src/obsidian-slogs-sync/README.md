# Slogs Sync for Obsidian

Slogs Sync synchronizes an Obsidian vault with the Slogs remote Obsidian Vault API.

## Install

### Community plugin

Slogs Sync is available as an Obsidian community plugin. Version `0.1.2` requires Obsidian `1.6.6` or newer.

1. Open Obsidian settings.
2. Go to Community plugins and turn off Restricted mode if needed.
3. Select Browse and search for `Slogs Sync`.
4. Install and enable the plugin.

### Beta testing

For unreleased test builds, use the GitHub releases in this repository or add this repository to BRAT. Normal users should install the community plugin instead of copying files into a vault.

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

Release assets are `manifest.json`, `main.js`, and `styles.css` if a future release adds styles. The GitHub release tag must match the `version` in `manifest.json`.
