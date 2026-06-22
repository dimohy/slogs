using System.Text;
using System.Text.Json.Serialization;

namespace Slogs.Data;

public sealed class AuthUser
{
    public const string AdminUserName = "admin";
    public const string AdminAuthorityUserName = "dimohy";

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [JsonIgnore]
    public string Password { get; set; } = string.Empty;

    public string ProfileImageUrl { get; set; } = string.Empty;

    public string Bio { get; set; } = string.Empty;

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public bool IsAdminMode { get; set; }

    public string AdminModeSourceUserName { get; set; } = string.Empty;

    public bool IsAdmin =>
        IsAdminMode
        || UserName.Equals(AdminAuthorityUserName, StringComparison.OrdinalIgnoreCase);

    public bool CanSwitchToAdminMode =>
        !IsAdminMode
        && UserName.Equals(AdminAuthorityUserName, StringComparison.OrdinalIgnoreCase);

    public bool CanExitAdminMode =>
        IsAdminMode
        && AdminModeSourceUserName.Equals(AdminAuthorityUserName, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> Badges =>
        IsAdmin
        || UserName.Equals(AdminUserName, StringComparison.OrdinalIgnoreCase)
            ? ["관리자", "시그니처"]
            : [];
}
