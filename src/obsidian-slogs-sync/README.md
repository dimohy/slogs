# Slogs Sync for Obsidian

Obsidian plugin that synchronizes an Obsidian vault with the Slogs Obsidian vault API.

## Requirements

- Obsidian desktop or mobile.
- A Slogs Bearer token with the `obsidian.sync` scope.
- Slogs server URL, normally `https://slogs.dev`.

## Build

```powershell
npm install
npm run check
npm run test
npm run build
```

## Manual Install

Copy `manifest.json` and `main.js` into an Obsidian vault plugin directory such as:

```text
<vault>/.obsidian/plugins/obsidian-slogs-sync/
```

Enable `Slogs Sync` from Obsidian community plugins, then set the Slogs server URL, `obsidian.sync` token, and remote vault name in plugin settings.

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
