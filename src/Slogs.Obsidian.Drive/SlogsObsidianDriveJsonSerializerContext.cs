using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slogs.Obsidian.Drive;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(SlogsObsidianDriveState))]
[JsonSerializable(typeof(SlogsObsidianDriveFileState))]
internal sealed partial class SlogsObsidianDriveJsonSerializerContext : JsonSerializerContext;
