export const OBSIDIAN_SCOPE_MARKDOWN = "markdown";
export const OBSIDIAN_SCOPE_ATTACHMENTS = "attachments";
export const OBSIDIAN_SCOPE_SETTINGS = "settings";
export const OBSIDIAN_KIND_MARKDOWN = "markdown";
export const OBSIDIAN_KIND_ATTACHMENT = "attachment";
export const OBSIDIAN_KIND_SETTING = "setting";
export const OBSIDIAN_ENCODING_UTF8 = "utf8";
export const OBSIDIAN_ENCODING_BASE64 = "base64";

export interface SlogsSyncFeatureFlags {
  syncAttachments: boolean;
  syncSettings: boolean;
}

export interface SlogsPathClassification {
  path: string;
  scope: string;
  kind: string;
  encoding: string;
}

export interface SlogsFileUpsertPayload {
  path: string;
  content: string;
  baseVersion: number | null;
  mediaType: string;
  scope: string;
  kind: string;
  encoding: string;
  metadataJson: string;
}

export function normalizeRemotePath(path: string): string {
  return path
    .replace(/\\/g, "/")
    .split("/")
    .map(part => part.trim())
    .filter(part => part.length > 0)
    .join("/");
}

export function isMarkdownPath(path: string): boolean {
  const normalized = normalizeRemotePath(path).toLowerCase();
  return normalized.endsWith(".md") && !normalized.startsWith(".obsidian/");
}

export function isSettingsPath(path: string): boolean {
  return normalizeRemotePath(path).toLowerCase().startsWith(".obsidian/");
}

export function isAttachmentPath(path: string): boolean {
  const normalized = normalizeRemotePath(path);
  return normalized.length > 0 && !isMarkdownPath(normalized) && !isSettingsPath(normalized);
}

export function shouldSyncPath(path: string, flags: SlogsSyncFeatureFlags): boolean {
  return isMarkdownPath(path)
    || (flags.syncAttachments && isAttachmentPath(path))
    || (flags.syncSettings && isSettingsPath(path));
}

export function classifyPath(path: string, flags: SlogsSyncFeatureFlags): SlogsPathClassification {
  const normalized = normalizeRemotePath(path);
  if (isMarkdownPath(normalized)) {
    return {
      path: normalized,
      scope: OBSIDIAN_SCOPE_MARKDOWN,
      kind: OBSIDIAN_KIND_MARKDOWN,
      encoding: OBSIDIAN_ENCODING_UTF8
    };
  }

  if (isSettingsPath(normalized)) {
    if (!flags.syncSettings) {
      throw new Error("Settings sync is not enabled.");
    }

    return {
      path: normalized,
      scope: OBSIDIAN_SCOPE_SETTINGS,
      kind: OBSIDIAN_KIND_SETTING,
      encoding: OBSIDIAN_ENCODING_UTF8
    };
  }

  if (!flags.syncAttachments) {
    throw new Error("Attachment sync is not enabled.");
  }

  return {
    path: normalized,
    scope: OBSIDIAN_SCOPE_ATTACHMENTS,
    kind: OBSIDIAN_KIND_ATTACHMENT,
    encoding: OBSIDIAN_ENCODING_BASE64
  };
}

export function buildUpsertPayload(
  path: string,
  content: string,
  baseVersion: number | null,
  mediaType: string,
  flags: SlogsSyncFeatureFlags,
  metadata: Record<string, string | number | boolean> = {}
): SlogsFileUpsertPayload {
  const classification = classifyPath(path, flags);
  return {
    path: classification.path,
    content,
    baseVersion,
    mediaType,
    scope: classification.scope,
    kind: classification.kind,
    encoding: classification.encoding,
    metadataJson: JSON.stringify(metadata)
  };
}

export function parseSimpleFrontmatter(markdown: string): Record<string, string> {
  const normalized = markdown.replace(/\r\n/g, "\n");
  if (!normalized.startsWith("---\n")) {
    return {};
  }

  const endIndex = normalized.indexOf("\n---", 4);
  if (endIndex < 0) {
    return {};
  }

  const result: Record<string, string> = {};
  const frontmatter = normalized.slice(4, endIndex);
  for (const line of frontmatter.split("\n")) {
    const separatorIndex = line.indexOf(":");
    if (separatorIndex <= 0) {
      continue;
    }

    const key = line.slice(0, separatorIndex).trim();
    const value = line.slice(separatorIndex + 1).trim().replace(/^['"]|['"]$/g, "");
    if (key.length > 0) {
      result[key] = value;
    }
  }

  return result;
}

export function frontmatterBoolean(markdown: string, keys: string[]): boolean {
  const frontmatter = parseSimpleFrontmatter(markdown);
  for (const key of keys) {
    const value = frontmatter[key];
    if (value === undefined) {
      continue;
    }

    return ["true", "yes", "1"].includes(value.trim().toLowerCase());
  }

  return false;
}

export function frontmatterValue(markdown: string, keys: string[]): string {
  const frontmatter = parseSimpleFrontmatter(markdown);
  for (const key of keys) {
    const value = frontmatter[key];
    if (value !== undefined && value.trim().length > 0) {
      return value.trim();
    }
  }

  return "";
}

export function guessMediaType(path: string): string {
  const lowerPath = normalizeRemotePath(path).toLowerCase();
  if (lowerPath.endsWith(".md")) {
    return "text/markdown";
  }

  if (lowerPath.endsWith(".json")) {
    return "application/json";
  }

  if (lowerPath.endsWith(".css")) {
    return "text/css";
  }

  if (lowerPath.endsWith(".png")) {
    return "image/png";
  }

  if (lowerPath.endsWith(".jpg") || lowerPath.endsWith(".jpeg")) {
    return "image/jpeg";
  }

  if (lowerPath.endsWith(".gif")) {
    return "image/gif";
  }

  if (lowerPath.endsWith(".webp")) {
    return "image/webp";
  }

  if (lowerPath.endsWith(".svg")) {
    return "image/svg+xml";
  }

  if (lowerPath.endsWith(".pdf")) {
    return "application/pdf";
  }

  return "application/octet-stream";
}
