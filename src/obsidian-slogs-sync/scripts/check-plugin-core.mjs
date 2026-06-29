import assert from "node:assert/strict";
import { mkdir } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import esbuild from "esbuild";

const __dirname = dirname(fileURLToPath(import.meta.url));
const pluginRoot = resolve(__dirname, "..");
const repoRoot = resolve(pluginRoot, "..", "..");
const outFile = resolve(repoRoot, "artifacts", "logs", "obsidian-plugin-core.bundle.mjs");
await mkdir(dirname(outFile), { recursive: true });

await esbuild.build({
  entryPoints: [resolve(pluginRoot, "plugin-core.ts")],
  bundle: true,
  format: "esm",
  platform: "node",
  outfile: outFile,
  logLevel: "silent"
});

const core = await import(`${pathToFileURL(outFile).href}?t=${Date.now()}`);
const configDir = ".slogs-config";

assert.equal(core.normalizeRemotePath("\\Notes\\ Daily.md "), "Notes/Daily.md");
assert.equal(core.isMarkdownPath("Notes/Daily.md", configDir), true);
assert.equal(core.isMarkdownPath(`${configDir}/app.json`, configDir), false);
assert.equal(core.shouldSyncPath("img/logo.png", { syncAttachments: false, syncSettings: false }, configDir), false);
assert.equal(core.shouldSyncPath("img/logo.png", { syncAttachments: true, syncSettings: false }, configDir), true);
assert.equal(core.shouldSyncPath(`${configDir}/app.json`, { syncAttachments: true, syncSettings: false }, configDir), false);
assert.equal(core.shouldSyncPath(`${configDir}/app.json`, { syncAttachments: true, syncSettings: true }, configDir), true);

const attachment = core.classifyPath("img/logo.png", { syncAttachments: true, syncSettings: false }, configDir);
assert.equal(attachment.scope, "attachments");
assert.equal(attachment.kind, "attachment");
assert.equal(attachment.encoding, "base64");

const settings = core.classifyPath(`${configDir}/app.json`, { syncAttachments: false, syncSettings: true }, configDir);
assert.equal(settings.scope, "settings");
assert.equal(settings.kind, "setting");
assert.equal(settings.encoding, "utf8");

const markdown = [
  "---",
  "slogs.post: true",
  "slogs.slug: obsidian-test",
  "slogs.llmWiki: yes",
  "---",
  "# Title"
].join("\n");
assert.equal(core.frontmatterBoolean(markdown, ["slogs.post"]), true);
assert.equal(core.frontmatterBoolean(markdown, ["slogs.llmWiki"]), true);
assert.equal(core.frontmatterValue(markdown, ["slogs.slug"]), "obsidian-test");

const payload = core.buildUpsertPayload(
  "Notes/Daily.md",
  "# Daily",
  3,
  "text/markdown",
  { syncAttachments: false, syncSettings: false },
  configDir,
  { client: "test" });
assert.deepEqual(
  {
    path: payload.path,
    baseVersion: payload.baseVersion,
    mediaType: payload.mediaType,
    scope: payload.scope,
    kind: payload.kind,
    encoding: payload.encoding,
    metadataJson: payload.metadataJson
  },
  {
    path: "Notes/Daily.md",
    baseVersion: 3,
    mediaType: "text/markdown",
    scope: "markdown",
    kind: "markdown",
    encoding: "utf8",
    metadataJson: "{\"client\":\"test\"}"
  });

console.log("obsidian plugin core checks passed");
