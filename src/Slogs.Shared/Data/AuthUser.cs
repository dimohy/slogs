using System.Text;
using System.Text.Json.Serialization;

namespace Slogs.Data;

public sealed class AuthUser
{
    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    [JsonIgnore]
    public string Password { get; set; } = string.Empty;

    public string ProfileImageUrl { get; set; } = string.Empty;

    public string Bio { get; set; } = string.Empty;

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public IReadOnlyList<string> Badges =>
        UserName.Equals("admin", StringComparison.OrdinalIgnoreCase)
            ? ["관리자", "시그니처"]
            : [];
}
