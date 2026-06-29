import {
  App,
  Modal,
  Notice,
  Plugin,
  PluginSettingTab,
  requestUrl,
  Setting,
  TFile,
  TFolder
} from "obsidian";
import {
  buildUpsertPayload,
  classifyPath,
  frontmatterBoolean,
  frontmatterValue,
  guessMediaType,
  isMarkdownPath,
  isSettingsPath,
  normalizeRemotePath,
  OBSIDIAN_ENCODING_BASE64,
  OBSIDIAN_ENCODING_UTF8,
  OBSIDIAN_SCOPE_ATTACHMENTS,
  OBSIDIAN_SCOPE_MARKDOWN,
  OBSIDIAN_SCOPE_SETTINGS,
  shouldSyncPath,
  type SlogsSyncFeatureFlags
} from "./plugin-core";

interface SlogsSyncSettings {
  serverUrl: string;
  token: string;
  vaultName: string;
  vaultId: string;
  clientId: string;
  lastRemoteVersion: number;
  syncAttachments: boolean;
  syncSettings: boolean;
  enablePostMapping: boolean;
  enableLlmWikiMapping: boolean;
  files: Record<string, SlogsSyncedFileState>;
}

interface SlogsSyncedFileState {
  version: number;
  contentHash: string;
  deleted: boolean;
  scope?: string;
  kind?: string;
}

interface SlogsVaultResponse {
  id: string;
  name: string;
  currentVersion: number;
}

interface SlogsFileListResponse {
  vaultId: string;
  currentVersion: number;
  files: SlogsFileResponse[];
  hasMore?: boolean;
  nextVersionCursor?: number | null;
}

interface SlogsFileResponse {
  id: string;
  vaultId: string;
  path: string;
  content: string;
  contentHash: string;
  mediaType: string;
  sizeBytes: number;
  version: number;
  isDeleted: boolean;
  scope?: string;
  kind?: string;
  encoding?: string;
  metadataJson?: string | null;
}

interface SlogsConflictResponse {
  error: string;
  remoteFile: SlogsFileResponse;
}

interface SlogsClientResponse {
  clientId: string;
  vaultId: string;
  clientName: string;
  clientKind: string;
  lastSeenVersion: number;
}

interface SlogsApiErrorResponse {
  error?: string;
}

interface LocalSyncFile {
  path: string;
  source: "vault" | "settings";
  file?: TFile;
}

type SyncApplyResult = "applied" | "conflict" | "skipped";
type PushResult = boolean | "conflict";
type ConflictAction = "useRemote" | "keepLocal" | "skip";

const DEFAULT_SETTINGS: SlogsSyncSettings = {
  serverUrl: "https://slogs.dev",
  token: "",
  vaultName: "",
  vaultId: "",
  clientId: "",
  lastRemoteVersion: 0,
  syncAttachments: false,
  syncSettings: false,
  enablePostMapping: false,
  enableLlmWikiMapping: false,
  files: {}
};

export default class SlogsSyncPlugin extends Plugin {
  settings: SlogsSyncSettings = { ...DEFAULT_SETTINGS };

  async onload(): Promise<void> {
    await this.loadSettings();
    if (!this.settings.clientId) {
      this.settings.clientId = crypto.randomUUID();
      await this.saveSettings();
    }

    this.addCommand({
      id: "sync-all",
      name: "Sync all Slogs files",
      callback: () => this.syncAll()
    });

    this.addCommand({
      id: "push-current-file",
      name: "Push current file to Slogs",
      callback: () => this.pushCurrentFile()
    });

    this.addCommand({
      id: "pull-remote-changes",
      name: "Pull remote changes from Slogs",
      callback: () => this.pullRemoteChanges()
    });

    this.addCommand({
      id: "open-slogs-post",
      name: "Open mapped Slogs post",
      callback: () => this.openCurrentSlogsPost()
    });

    this.addCommand({
      id: "open-slogs-vault-settings",
      name: "Open Slogs vault settings",
      callback: () => window.open(`${this.getServerUrl()}/me/settings`, "_blank")
    });

    this.addSettingTab(new SlogsSyncSettingTab(this.app, this));
  }

  async loadSettings(): Promise<void> {
    const loaded = await this.loadData() as Partial<SlogsSyncSettings> | null;
    this.settings = {
      ...DEFAULT_SETTINGS,
      ...loaded,
      files: loaded?.files ?? {}
    };
  }

  async saveSettings(): Promise<void> {
    await this.saveData(this.settings);
  }

  async syncAll(): Promise<void> {
    try {
      await this.ensureRemoteVault();
      const pullResult = await this.pullRemoteChanges(false);
      const pushResult = await this.pushLocalChanges(false);
      new Notice(`Slogs sync complete. Pulled ${pullResult.applied}, pushed ${pushResult.pushed}, conflicts ${pullResult.conflicts + pushResult.conflicts}.`);
    } catch (error) {
      new Notice(`Slogs sync failed: ${toErrorMessage(error)}`);
    }
  }

  async pushCurrentFile(): Promise<void> {
    const activeFile = this.app.workspace.getActiveFile();
    if (!(activeFile instanceof TFile) || !shouldSyncPath(activeFile.path, this.getFeatureFlags())) {
      new Notice("Open a synced Markdown file or enabled attachment before pushing to Slogs.");
      return;
    }

    try {
      await this.ensureRemoteVault();
      const pushed = await this.pushLocalFile({ path: activeFile.path, source: "vault", file: activeFile });
      new Notice(pushed === true ? "Current file pushed to Slogs." : pushed === "conflict" ? "Current file has a Slogs conflict." : "Current file already matches Slogs.");
    } catch (error) {
      new Notice(`Slogs push failed: ${toErrorMessage(error)}`);
    }
  }

  async pullRemoteChanges(showNotice = true): Promise<{ applied: number; conflicts: number }> {
    try {
      const vault = await this.ensureRemoteVault();
      let sinceVersion = this.settings.lastRemoteVersion;
      let applied = 0;
      let conflicts = 0;
      let hasMore = true;

      while (hasMore) {
        const response = await this.requestJson<SlogsFileListResponse>(
          "GET",
          `/api/obsidian/vaults/${encodeURIComponent(vault.id)}/files?sinceVersion=${sinceVersion}&includeDeleted=true&limit=500&scopes=${encodeURIComponent(this.getEnabledScopes().join(","))}`);

        for (const remoteFile of response.files) {
          const result = await this.applyRemoteFile(remoteFile);
          if (result === "applied") {
            applied += 1;
          } else if (result === "conflict") {
            conflicts += 1;
          }
        }

        hasMore = response.hasMore === true && response.nextVersionCursor !== null && response.nextVersionCursor !== undefined;
        sinceVersion = response.nextVersionCursor ?? response.currentVersion;
      }

      if (conflicts === 0) {
        this.settings.lastRemoteVersion = Math.max(this.settings.lastRemoteVersion, sinceVersion);
      }

      await this.saveSettings();
      await this.heartbeat();
      if (showNotice) {
        new Notice(`Pulled ${applied} Slogs changes. Conflicts ${conflicts}.`);
      }

      return { applied, conflicts };
    } catch (error) {
      if (showNotice) {
        new Notice(`Slogs pull failed: ${toErrorMessage(error)}`);
      }

      throw error;
    }
  }

  async pushLocalChanges(showNotice = true): Promise<{ pushed: number; conflicts: number }> {
    try {
      await this.ensureRemoteVault();
      const localFiles = await this.collectLocalFiles();
      const localPaths = new Set(localFiles.map(file => normalizeRemotePath(file.path)));
      let pushed = 0;
      let conflicts = 0;

      for (const file of localFiles) {
        const changed = await this.pushLocalFile(file);
        if (changed === true) {
          pushed += 1;
        } else if (changed === "conflict") {
          conflicts += 1;
        }
      }

      for (const [path, state] of Object.entries(this.settings.files)) {
        if (state.deleted || localPaths.has(path)) {
          continue;
        }

        const deleted = await this.pushDeletedFile(path, state.version, state.scope ?? OBSIDIAN_SCOPE_MARKDOWN);
        if (deleted === true) {
          pushed += 1;
        } else if (deleted === "conflict") {
          conflicts += 1;
        }
      }

      await this.saveSettings();
      await this.heartbeat();
      if (showNotice) {
        new Notice(`Pushed ${pushed} Slogs changes. Conflicts ${conflicts}.`);
      }

      return { pushed, conflicts };
    } catch (error) {
      if (showNotice) {
        new Notice(`Slogs push failed: ${toErrorMessage(error)}`);
      }

      throw error;
    }
  }

  private async collectLocalFiles(): Promise<LocalSyncFile[]> {
    const flags = this.getFeatureFlags();
    const files: LocalSyncFile[] = [];
    for (const file of this.app.vault.getFiles()) {
      if (shouldSyncPath(file.path, flags) && !isSettingsPath(file.path)) {
        files.push({ path: normalizeRemotePath(file.path), source: "vault", file });
      }
    }

    if (flags.syncSettings) {
      for (const path of await this.listSettingsFiles(".obsidian")) {
        files.push({ path, source: "settings" });
      }
    }

    return files;
  }

  private async pushLocalFile(localFile: LocalSyncFile, forceBaseVersion?: number): Promise<PushResult> {
    const flags = this.getFeatureFlags();
    const classification = classifyPath(localFile.path, flags);
    const path = classification.path;
    const state = this.settings.files[path];
    const content = await this.readLocalContent(localFile, classification.encoding);
    const contentHash = classification.encoding === OBSIDIAN_ENCODING_BASE64
      ? await sha256HexBinary(base64ToArrayBuffer(content))
      : await sha256HexText(content);
    if (forceBaseVersion === undefined && state !== undefined && !state.deleted && state.contentHash === contentHash) {
      return false;
    }

    const vault = await this.ensureRemoteVault();
    const payload = buildUpsertPayload(
      path,
      content,
      forceBaseVersion ?? state?.version ?? null,
      guessMediaType(path),
      flags,
      {
        client: "obsidian-plugin",
        source: localFile.source
      });
    const response = await this.requestJsonOrConflict<SlogsFileResponse>(
      "PUT",
      `/api/obsidian/vaults/${encodeURIComponent(vault.id)}/files`,
      payload);

    if (isConflictResponse(response)) {
      return await this.resolvePushConflict(localFile, response.remoteFile);
    }

    this.rememberRemoteFile(response);
    if (classification.scope === OBSIDIAN_SCOPE_MARKDOWN) {
      await this.runMappings(path, content);
    }

    return true;
  }

  private async resolvePushConflict(localFile: LocalSyncFile, remoteFile: SlogsFileResponse): Promise<PushResult> {
    const action = await new SlogsConflictModal(
      this.app,
      normalizeRemotePath(remoteFile.path),
      "Slogs remote content changed after this local file was last synced."
    ).openAndGetChoice();

    if (action === "useRemote") {
      await this.applyRemoteFile(remoteFile, true);
      return "conflict";
    }

    if (action === "keepLocal") {
      const retry = await this.pushLocalFile(localFile, remoteFile.version);
      return retry === "conflict" ? "conflict" : true;
    }

    return "conflict";
  }

  private async pushDeletedFile(path: string, baseVersion: number, scope: string): Promise<PushResult> {
    const vault = await this.ensureRemoteVault();
    const response = await this.requestJsonOrConflict<SlogsFileResponse>(
      "POST",
      `/api/obsidian/vaults/${encodeURIComponent(vault.id)}/files/delete`,
      {
        path,
        baseVersion,
        scope
      });

    if (isConflictResponse(response)) {
      const action = await new SlogsConflictModal(
        this.app,
        path,
        "Slogs remote content changed before this local delete was pushed."
      ).openAndGetChoice();
      if (action === "keepLocal") {
        return await this.pushDeletedFile(path, response.remoteFile.version, response.remoteFile.scope ?? scope);
      }

      if (action === "useRemote") {
        await this.applyRemoteFile(response.remoteFile, true);
      }

      return "conflict";
    }

    this.rememberRemoteFile(response);
    return true;
  }

  private async applyRemoteFile(remoteFile: SlogsFileResponse, force = false): Promise<SyncApplyResult> {
    const path = normalizeRemotePath(remoteFile.path);
    const scope = remoteFile.scope ?? OBSIDIAN_SCOPE_MARKDOWN;
    if (!this.isRemoteScopeEnabled(scope)) {
      return "skipped";
    }

    const state = this.settings.files[path];
    const localExists = await this.localExists(path);
    const localHash = localExists ? await this.computeLocalHash(path, remoteFile.encoding ?? OBSIDIAN_ENCODING_UTF8) : "";
    const isDirty = localExists && ((!state && localHash !== remoteFile.contentHash)
      || (state && localHash !== state.contentHash && remoteFile.version !== state.version));

    if (!force && isDirty) {
      const action = await new SlogsConflictModal(
        this.app,
        path,
        remoteFile.isDeleted
          ? "Slogs deleted this file, but your local copy has unsynced changes."
          : "Slogs has a remote change, but your local copy has unsynced changes."
      ).openAndGetChoice();
      if (action !== "useRemote") {
        return "conflict";
      }
    }

    if (remoteFile.isDeleted) {
      if (localExists) {
        await this.deleteLocalPath(path);
      }

      this.rememberRemoteFile(remoteFile);
      return "applied";
    }

    await this.writeLocalContent(path, remoteFile.content, remoteFile.encoding ?? OBSIDIAN_ENCODING_UTF8);
    this.rememberRemoteFile(remoteFile);
    return "applied";
  }

  private async readLocalContent(localFile: LocalSyncFile, encoding: string): Promise<string> {
    if (localFile.source === "settings") {
      return await this.app.vault.adapter.read(localFile.path);
    }

    if (!localFile.file) {
      throw new Error(`Missing local file object: ${localFile.path}`);
    }

    if (encoding === OBSIDIAN_ENCODING_BASE64) {
      return arrayBufferToBase64(await this.app.vault.readBinary(localFile.file));
    }

    return await this.app.vault.read(localFile.file);
  }

  private async localExists(path: string): Promise<boolean> {
    if (isSettingsPath(path)) {
      return await this.app.vault.adapter.exists(path);
    }

    return this.app.vault.getAbstractFileByPath(path) instanceof TFile;
  }

  private async computeLocalHash(path: string, encoding: string): Promise<string> {
    if (isSettingsPath(path)) {
      return sha256HexText(await this.app.vault.adapter.read(path));
    }

    const file = this.app.vault.getAbstractFileByPath(path);
    if (!(file instanceof TFile)) {
      return "";
    }

    return encoding === OBSIDIAN_ENCODING_BASE64
      ? sha256HexBinary(await this.app.vault.readBinary(file))
      : sha256HexText(await this.app.vault.read(file));
  }

  private async writeLocalContent(path: string, content: string, encoding: string): Promise<void> {
    if (isSettingsPath(path)) {
      await this.ensureAdapterFolder(path);
      await this.app.vault.adapter.write(path, content);
      return;
    }

    const existing = this.app.vault.getAbstractFileByPath(path);
    await this.ensureFolder(path);
    if (encoding === OBSIDIAN_ENCODING_BASE64) {
      const bytes = base64ToArrayBuffer(content);
      if (existing instanceof TFile) {
        await this.app.vault.modifyBinary(existing, bytes);
      } else if (existing === null) {
        await this.app.vault.createBinary(path, bytes);
      } else {
        throw new Error(`Path segment is not a file: ${path}`);
      }

      return;
    }

    if (existing instanceof TFile) {
      await this.app.vault.modify(existing, content);
    } else if (existing === null) {
      await this.app.vault.create(path, content);
    } else {
      throw new Error(`Path segment is not a file: ${path}`);
    }
  }

  private async deleteLocalPath(path: string): Promise<void> {
    if (isSettingsPath(path)) {
      if (await this.app.vault.adapter.exists(path)) {
        await this.app.vault.adapter.remove(path);
      }

      return;
    }

    const existing = this.app.vault.getAbstractFileByPath(path);
    if (existing instanceof TFile) {
      await this.app.vault.delete(existing);
    }
  }

  private async runMappings(path: string, content: string): Promise<void> {
    if (!this.settings.enablePostMapping && !this.settings.enableLlmWikiMapping) {
      return;
    }

    const vault = await this.ensureRemoteVault();
    if (this.settings.enablePostMapping && frontmatterBoolean(content, ["slogs.post", "slogsPost"])) {
      await this.requestJson(
        "POST",
        `/api/obsidian/vaults/${encodeURIComponent(vault.id)}/files/map-post`,
        { path });
    }

    if (this.settings.enableLlmWikiMapping && frontmatterBoolean(content, ["slogs.llmWiki", "slogs.llmwiki", "llmWiki"])) {
      await this.requestJson(
        "POST",
        `/api/obsidian/vaults/${encodeURIComponent(vault.id)}/files/map-llm-wiki`,
        { path });
    }
  }

  private async openCurrentSlogsPost(): Promise<void> {
    const activeFile = this.app.workspace.getActiveFile();
    if (!(activeFile instanceof TFile) || !isMarkdownPath(activeFile.path)) {
      new Notice("Open a mapped Markdown note first.");
      return;
    }

    const content = await this.app.vault.read(activeFile);
    const slug = frontmatterValue(content, ["slogs.slug", "slug"]);
    if (!slug) {
      new Notice("This note does not have a Slogs slug frontmatter field.");
      return;
    }

    window.open(`${this.getServerUrl()}/post/${encodeURIComponent(slug)}`, "_blank");
  }

  private rememberRemoteFile(remoteFile: SlogsFileResponse): void {
    const path = normalizeRemotePath(remoteFile.path);
    this.settings.files[path] = {
      version: remoteFile.version,
      contentHash: remoteFile.contentHash,
      deleted: remoteFile.isDeleted,
      scope: remoteFile.scope ?? OBSIDIAN_SCOPE_MARKDOWN,
      kind: remoteFile.kind
    };

    this.settings.lastRemoteVersion = Math.max(this.settings.lastRemoteVersion, remoteFile.version);
  }

  private async ensureRemoteVault(): Promise<SlogsVaultResponse> {
    this.ensureConfigured();
    if (this.settings.vaultId) {
      return {
        id: this.settings.vaultId,
        name: this.getVaultName(),
        currentVersion: this.settings.lastRemoteVersion
      };
    }

    const vault = await this.requestJson<SlogsVaultResponse>(
      "POST",
      "/api/obsidian/vaults",
      { name: this.getVaultName() });

    this.settings.vaultId = vault.id;
    this.settings.vaultName = vault.name;
    this.settings.lastRemoteVersion = Math.max(this.settings.lastRemoteVersion, vault.currentVersion);
    await this.saveSettings();
    await this.heartbeat();
    return vault;
  }

  private async heartbeat(): Promise<SlogsClientResponse | null> {
    if (!this.settings.vaultId) {
      return null;
    }

    return await this.requestJson<SlogsClientResponse>(
      "POST",
      `/api/obsidian/vaults/${encodeURIComponent(this.settings.vaultId)}/clients/heartbeat`,
      {
        clientId: this.settings.clientId,
        clientName: "Obsidian plugin",
        clientKind: "obsidian-plugin",
        lastSeenVersion: this.settings.lastRemoteVersion
      });
  }

  private ensureConfigured(): void {
    if (!this.settings.serverUrl.trim()) {
      throw new Error("Set the Slogs server URL first.");
    }

    if (!this.settings.token.trim()) {
      throw new Error("Set a Slogs obsidian.sync Bearer token first.");
    }
  }

  private getVaultName(): string {
    const configuredName = this.settings.vaultName.trim();
    if (configuredName) {
      return configuredName;
    }

    const vaultWithName = this.app.vault as unknown as { getName?: () => string };
    return vaultWithName.getName?.() ?? "Obsidian Vault";
  }

  private async ensureFolder(path: string): Promise<void> {
    const parts = normalizeRemotePath(path).split("/");
    parts.pop();
    let currentPath = "";

    for (const part of parts) {
      currentPath = currentPath ? `${currentPath}/${part}` : part;
      const existing = this.app.vault.getAbstractFileByPath(currentPath);
      if (existing instanceof TFolder) {
        continue;
      }

      if (existing !== null) {
        throw new Error(`Path segment is not a folder: ${currentPath}`);
      }

      await this.app.vault.createFolder(currentPath);
    }
  }

  private async ensureAdapterFolder(path: string): Promise<void> {
    const parts = normalizeRemotePath(path).split("/");
    parts.pop();
    let currentPath = "";

    for (const part of parts) {
      currentPath = currentPath ? `${currentPath}/${part}` : part;
      if (await this.app.vault.adapter.exists(currentPath)) {
        continue;
      }

      await this.app.vault.adapter.mkdir(currentPath);
    }
  }

  private async listSettingsFiles(path: string): Promise<string[]> {
    if (!(await this.app.vault.adapter.exists(path))) {
      return [];
    }

    const result: string[] = [];
    const listed = await this.app.vault.adapter.list(path);
    result.push(...listed.files.map(normalizeRemotePath));
    for (const folder of listed.folders) {
      result.push(...await this.listSettingsFiles(folder));
    }

    return result;
  }

  private async requestJson<T>(method: string, path: string, body?: unknown): Promise<T> {
    const response = await requestUrl({
      url: `${this.getServerUrl()}${path}`,
      method,
      headers: this.buildHeaders(body !== undefined),
      body: body === undefined ? undefined : JSON.stringify(body),
      throw: false
    });

    if (response.status < 200 || response.status >= 300) {
      const error = response.json as SlogsApiErrorResponse | undefined;
      throw new Error(error?.error ?? `HTTP ${response.status}`);
    }

    return response.json as T;
  }

  private async requestJsonOrConflict<T>(method: string, path: string, body: unknown): Promise<T | SlogsConflictResponse> {
    const response = await requestUrl({
      url: `${this.getServerUrl()}${path}`,
      method,
      headers: this.buildHeaders(true),
      body: JSON.stringify(body),
      throw: false
    });

    if (response.status === 409) {
      return response.json as SlogsConflictResponse;
    }

    if (response.status < 200 || response.status >= 300) {
      const error = response.json as SlogsApiErrorResponse | undefined;
      throw new Error(error?.error ?? `HTTP ${response.status}`);
    }

    return response.json as T;
  }

  private buildHeaders(hasBody: boolean): Record<string, string> {
    const headers: Record<string, string> = {
      Authorization: `Bearer ${this.settings.token.trim()}`
    };

    if (hasBody) {
      headers["Content-Type"] = "application/json";
    }

    return headers;
  }

  private getServerUrl(): string {
    return this.settings.serverUrl.trim().replace(/\/+$/, "");
  }

  private getFeatureFlags(): SlogsSyncFeatureFlags {
    return {
      syncAttachments: this.settings.syncAttachments,
      syncSettings: this.settings.syncSettings
    };
  }

  private getEnabledScopes(): string[] {
    const scopes = [OBSIDIAN_SCOPE_MARKDOWN];
    if (this.settings.syncAttachments) {
      scopes.push(OBSIDIAN_SCOPE_ATTACHMENTS);
    }

    if (this.settings.syncSettings) {
      scopes.push(OBSIDIAN_SCOPE_SETTINGS);
    }

    return scopes;
  }

  private isRemoteScopeEnabled(scope: string): boolean {
    return scope === OBSIDIAN_SCOPE_MARKDOWN
      || (scope === OBSIDIAN_SCOPE_ATTACHMENTS && this.settings.syncAttachments)
      || (scope === OBSIDIAN_SCOPE_SETTINGS && this.settings.syncSettings);
  }
}

class SlogsSyncSettingTab extends PluginSettingTab {
  constructor(app: App, private readonly plugin: SlogsSyncPlugin) {
    super(app, plugin);
  }

  display(): void {
    const { containerEl } = this;
    containerEl.empty();

    new Setting(containerEl)
      .setName("Slogs server URL")
      .setDesc("Remote Slogs server that stores the user-scoped Obsidian vault.")
      .addText(text => text
        .setPlaceholder("https://slogs.dev")
        .setValue(this.plugin.settings.serverUrl)
        .onChange(async value => {
          this.plugin.settings.serverUrl = value.trim();
          await this.plugin.saveSettings();
        }));

    new Setting(containerEl)
      .setName("Bearer token")
      .setDesc("Create a Slogs Obsidian sync token with the obsidian.sync scope and paste it here.")
      .addText(text => {
        text.inputEl.type = "password";
        text
          .setPlaceholder("slogs token")
          .setValue(this.plugin.settings.token)
          .onChange(async value => {
            this.plugin.settings.token = value.trim();
            await this.plugin.saveSettings();
          });
      });

    new Setting(containerEl)
      .setName("Remote vault name")
      .setDesc("Leave empty to use the current Obsidian vault name.")
      .addText(text => text
        .setPlaceholder("My Obsidian Vault")
        .setValue(this.plugin.settings.vaultName)
        .onChange(async value => {
          this.plugin.settings.vaultName = value.trim();
          this.plugin.settings.vaultId = "";
          this.plugin.settings.lastRemoteVersion = 0;
          this.plugin.settings.files = {};
          await this.plugin.saveSettings();
        }));

    new Setting(containerEl)
      .setName("Sync attachments")
      .setDesc("Opt in to binary attachment sync. Attachments are stored as base64 content through the Slogs Obsidian API.")
      .addToggle(toggle => toggle
        .setValue(this.plugin.settings.syncAttachments)
        .onChange(async value => {
          this.plugin.settings.syncAttachments = value;
          await this.plugin.saveSettings();
        }));

    new Setting(containerEl)
      .setName("Sync .obsidian settings")
      .setDesc("Opt in to .obsidian settings sync after attachment sync is enabled.")
      .addToggle(toggle => toggle
        .setValue(this.plugin.settings.syncSettings)
        .onChange(async value => {
          this.plugin.settings.syncSettings = value;
          await this.plugin.saveSettings();
        }));

    new Setting(containerEl)
      .setName("Map notes to Slogs posts")
      .setDesc("When enabled, notes with slogs.post: true frontmatter are mapped through the Slogs post API after push.")
      .addToggle(toggle => toggle
        .setValue(this.plugin.settings.enablePostMapping)
        .onChange(async value => {
          this.plugin.settings.enablePostMapping = value;
          await this.plugin.saveSettings();
        }));

    new Setting(containerEl)
      .setName("Map notes to Slogs LLM Wiki")
      .setDesc("When enabled, notes with slogs.llmWiki: true frontmatter are imported through the Slogs LLM Wiki API after push.")
      .addToggle(toggle => toggle
        .setValue(this.plugin.settings.enableLlmWikiMapping)
        .onChange(async value => {
          this.plugin.settings.enableLlmWikiMapping = value;
          await this.plugin.saveSettings();
        }));

    new Setting(containerEl)
      .setName("Reset local sync state")
      .setDesc("Keeps local files and the Slogs remote vault, but forgets this plugin client's cursor.")
      .addButton(button => button
        .setButtonText("Reset")
        .onClick(async () => {
          this.plugin.settings.vaultId = "";
          this.plugin.settings.lastRemoteVersion = 0;
          this.plugin.settings.files = {};
          await this.plugin.saveSettings();
          new Notice("Slogs local sync state reset.");
        }));
  }
}

class SlogsConflictModal extends Modal {
  private resolved = false;
  private resolver: (value: ConflictAction) => void = () => undefined;

  constructor(app: App, private readonly path: string, private readonly description: string) {
    super(app);
  }

  openAndGetChoice(): Promise<ConflictAction> {
    return new Promise(resolve => {
      this.resolver = resolve;
      this.open();
    });
  }

  onOpen(): void {
    const { contentEl } = this;
    contentEl.empty();
    contentEl.createEl("h2", { text: "Slogs sync conflict" });
    contentEl.createEl("p", { text: this.path });
    contentEl.createEl("p", { text: this.description });

    new Setting(contentEl)
      .addButton(button => button
        .setButtonText("Use remote")
        .setCta()
        .onClick(() => this.choose("useRemote")))
      .addButton(button => button
        .setButtonText("Keep local")
        .onClick(() => this.choose("keepLocal")))
      .addButton(button => button
        .setButtonText("Skip")
        .onClick(() => this.choose("skip")));
  }

  onClose(): void {
    this.contentEl.empty();
    if (!this.resolved) {
      this.choose("skip");
    }
  }

  private choose(action: ConflictAction): void {
    if (this.resolved) {
      return;
    }

    this.resolved = true;
    this.resolver(action);
    this.close();
  }
}

async function sha256HexText(content: string): Promise<string> {
  const bytes = new TextEncoder().encode(content);
  return await sha256HexBinary(bytes.buffer);
}

async function sha256HexBinary(content: BufferSource): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", content);
  return Array.from(new Uint8Array(digest))
    .map(byte => byte.toString(16).padStart(2, "0"))
    .join("");
}

function arrayBufferToBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  const chunkSize = 0x8000;
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + chunkSize));
  }

  return btoa(binary);
}

function base64ToArrayBuffer(base64: string): ArrayBuffer {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }

  return bytes.buffer;
}

function isConflictResponse(value: unknown): value is SlogsConflictResponse {
  return typeof value === "object"
    && value !== null
    && "error" in value
    && (value as SlogsConflictResponse).error === "obsidianConflict"
    && "remoteFile" in value;
}

function toErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
