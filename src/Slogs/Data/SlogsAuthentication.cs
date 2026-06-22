using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Slogs.Data;

public static class SlogsAuthentication
{
    private const string ProfileImageClaim = "slogs:profile-image";
    private const string BioClaim = "slogs:bio";
    private const string RegisteredAtClaim = "slogs:registered-at";
    private const string AdminModeClaim = "slogs:admin-mode";
    private const string AdminModeSourceUserNameClaim = "slogs:admin-source-user";
    public static TimeSpan PersistentSessionLifetime { get; } = TimeSpan.FromDays(30);

    public static ClaimsPrincipal CreatePrincipal(AuthUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserName),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ProfileImageClaim, user.ProfileImageUrl),
            new(BioClaim, user.Bio),
            new(RegisteredAtClaim, user.RegisteredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
        };

        if (user.IsAdminMode)
        {
            claims.Add(new Claim(AdminModeClaim, bool.TrueString));
            claims.Add(new Claim(AdminModeSourceUserNameClaim, user.AdminModeSourceUserName));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public static Task SignInPersistentAsync(HttpContext httpContext, AuthUser user)
        => httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            CreatePrincipal(user),
            CreatePersistentProperties());

    public static AuthenticationProperties CreatePersistentProperties()
        => new()
        {
            IsPersistent = true,
            AllowRefresh = true
        };

    public static AuthUser? TryCreateUser(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userName = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var isAdminMode = bool.TryParse(principal.FindFirstValue(AdminModeClaim), out var parsedAdminMode)
            && parsedAdminMode;
        var adminModeSourceUserName = principal.FindFirstValue(AdminModeSourceUserNameClaim) ?? string.Empty;
        if (userName.Equals(AuthUser.AdminUserName, StringComparison.OrdinalIgnoreCase)
            && (!isAdminMode || !adminModeSourceUserName.Equals(AuthUser.AdminAuthorityUserName, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var registeredAt = DateTime.UtcNow;
        var registeredAtValue = principal.FindFirstValue(RegisteredAtClaim);
        if (!string.IsNullOrWhiteSpace(registeredAtValue)
            && DateTime.TryParse(
                registeredAtValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedRegisteredAt))
        {
            registeredAt = parsedRegisteredAt;
        }

        return new AuthUser
        {
            UserName = userName,
            DisplayName = principal.FindFirstValue(ClaimTypes.Name) ?? userName,
            Email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            ProfileImageUrl = principal.FindFirstValue(ProfileImageClaim) ?? string.Empty,
            Bio = principal.FindFirstValue(BioClaim) ?? string.Empty,
            RegisteredAt = registeredAt,
            IsAdminMode = isAdminMode,
            AdminModeSourceUserName = isAdminMode ? adminModeSourceUserName : string.Empty
        };
    }
}
