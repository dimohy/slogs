using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Net.Mail;

namespace Slogs.Data;

public sealed class AuthService(IDbContextFactory<SlogsDbContext> dbFactory, IHttpContextAccessor httpContextAccessor)
{
    private readonly object stateLock = new();
    private AuthUser? currentUser = SlogsAuthentication.TryCreateUser(httpContextAccessor.HttpContext?.User);

    public AuthUser? CurrentUser
    {
        get { lock (stateLock) { return currentUser; } }
        private set { lock (stateLock) { currentUser = value; } }
    }

    public event Action? AuthStateChanged;

    public Task<AuthUser?> GetCurrentUserAsync()
    {
        lock (stateLock)
        {
            return Task.FromResult(currentUser);
        }
    }

    public Task<bool> IsSignedInAsync()
    {
        lock (stateLock)
        {
            return Task.FromResult(currentUser is not null);
        }
    }

    public async Task<AuthUser?> LoginAsync(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var normalized = NormalizeUser(userName);
        if (normalized.Equals(AuthUser.AdminUserName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var matchedUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserName == normalized);
        if (matchedUser is null || !matchedUser.Password.Equals(password.Trim(), StringComparison.Ordinal))
        {
            return null;
        }

        CurrentUser = ToModel(matchedUser);
        AuthStateChanged?.Invoke();
        return CurrentUser;
    }

    public async Task<AuthUser> RegisterAsync(string userName, string displayName, string password)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var normalized = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("아이디를 입력해 주세요.");
        }

        if (normalized.Equals(AuthUser.AdminUserName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("reservedUserName");
        }

        if (await db.Users.AnyAsync(x => x.UserName == normalized))
        {
            throw new InvalidOperationException("이미 사용 중인 아이디입니다.");
        }

        var newUser = new UserRecord
        {
            UserName = normalized,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName.Trim() : displayName.Trim(),
            Email = string.Empty,
            Password = password,
            ProfileImageUrl = string.Empty,
            Bio = "slogs에서 새 글을 준비 중인 작성자입니다.",
            RegisteredAt = DateTime.UtcNow
        };

        db.Users.Add(newUser);
        await db.SaveChangesAsync();

        CurrentUser = ToModel(newUser);
        AuthStateChanged?.Invoke();
        return CurrentUser;
    }

    public async Task<AuthUser> EnterAdminModeAsync(AuthUser currentUser)
    {
        if (!currentUser.CanSwitchToAdminMode)
        {
            throw new InvalidOperationException("adminModeForbidden");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var admin = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserName == AuthUser.AdminUserName)
            ?? throw new InvalidOperationException("adminUserNotFound");

        var adminUser = ToModel(admin);
        adminUser.IsAdminMode = true;
        adminUser.AdminModeSourceUserName = currentUser.UserName;

        CurrentUser = adminUser;
        AuthStateChanged?.Invoke();
        return adminUser;
    }

    public async Task<AuthUser> ExitAdminModeAsync(AuthUser currentUser)
    {
        if (!currentUser.CanExitAdminMode)
        {
            throw new InvalidOperationException("adminModeForbidden");
        }

        var sourceUser = await GetUserAsync(currentUser.AdminModeSourceUserName)
            ?? throw new InvalidOperationException("adminModeSourceNotFound");

        CurrentUser = sourceUser;
        AuthStateChanged?.Invoke();
        return sourceUser;
    }

    public async Task<AuthUser?> LoginExternalAsync(
        string provider,
        string providerUserId,
        string? email,
        string? displayName,
        string? profileImageUrl)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedProviderUserId = providerUserId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedProvider) || string.IsNullOrWhiteSpace(normalizedProviderUserId))
        {
            throw new InvalidOperationException("externalLoginInvalid");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var externalLogin = await db.ExternalLogins.FindAsync(normalizedProvider, normalizedProviderUserId);
        if (externalLogin is null)
        {
            return null;
        }

        if (externalLogin.UserName.Equals(AuthUser.AdminUserName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        var finalDisplayName = NormalizeDisplayName(displayName, normalizedEmail);
        var finalProfileImageUrl = profileImageUrl?.Trim() ?? string.Empty;

        externalLogin.Email = normalizedEmail;
        externalLogin.LastLoginAt = now;

        var existingUser = await db.Users.FirstOrDefaultAsync(x => x.UserName == externalLogin.UserName)
            ?? await RecreateExternalUserAsync(db, externalLogin.UserName, normalizedEmail, finalDisplayName, finalProfileImageUrl, now);
        RefreshExternalProfile(existingUser, normalizedEmail, finalDisplayName, finalProfileImageUrl);

        await db.SaveChangesAsync();
        CurrentUser = ToModel(existingUser);
        AuthStateChanged?.Invoke();
        return CurrentUser;
    }

    public async Task<string> CreateExternalUserNameCandidateAsync(string provider, string? email, string? displayName)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await CreateUniqueExternalUserNameAsync(
            db,
            NormalizeProvider(provider),
            email?.Trim().ToLowerInvariant() ?? string.Empty,
            NormalizeDisplayName(displayName, email?.Trim().ToLowerInvariant() ?? string.Empty));
    }

    public async Task<AuthUser> CreateConfirmedExternalLoginAsync(
        string provider,
        string providerUserId,
        string? email,
        string? displayName,
        string? profileImageUrl,
        string requestedUserName)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedProviderUserId = providerUserId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedProvider) || string.IsNullOrWhiteSpace(normalizedProviderUserId))
        {
            throw new InvalidOperationException("externalLoginInvalid");
        }

        var userName = NormalizeProfileUserName(requestedUserName, string.Empty);
        if (userName.Equals(AuthUser.AdminUserName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("externalUserNameTaken");
        }

        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        var finalDisplayName = NormalizeDisplayName(displayName, normalizedEmail);
        var finalProfileImageUrl = profileImageUrl?.Trim() ?? string.Empty;

        await using var db = await dbFactory.CreateDbContextAsync();
        var externalLogin = await db.ExternalLogins.FindAsync(normalizedProvider, normalizedProviderUserId);
        if (externalLogin is not null)
        {
            return await LoginExternalAsync(provider, providerUserId, email, displayName, profileImageUrl)
                ?? throw new InvalidOperationException("externalLoginInvalid");
        }

        if (await db.Users.AsNoTracking().AnyAsync(x => x.UserName == userName))
        {
            throw new InvalidOperationException("externalUserNameTaken");
        }

        var now = DateTime.UtcNow;
        var newUser = new UserRecord
        {
            UserName = userName,
            DisplayName = finalDisplayName,
            Email = normalizedEmail,
            Password = string.Empty,
            ProfileImageUrl = finalProfileImageUrl,
            Bio = $"{GetProviderDisplayName(normalizedProvider)} 계정으로 가입한 슬로거입니다.",
            RegisteredAt = now
        };

        db.Users.Add(newUser);
        db.ExternalLogins.Add(new ExternalLoginRecord
        {
            Provider = normalizedProvider,
            ProviderUserId = normalizedProviderUserId,
            UserName = userName,
            Email = normalizedEmail,
            CreatedAt = now,
            LastLoginAt = now
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new InvalidOperationException("externalUserNameTaken", ex);
        }

        CurrentUser = ToModel(newUser);
        AuthStateChanged?.Invoke();
        return CurrentUser;
    }

    public async Task<AuthUser> UpdateProfileAsync(
        string userName,
        string displayName,
        string? email,
        string? profileImageUrl,
        string? bio)
    {
        var normalizedUserName = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(normalizedUserName))
        {
            throw new InvalidOperationException("profileUserRequired");
        }

        var normalizedDisplayName = NormalizeProfileDisplayName(displayName);
        var normalizedEmail = NormalizeProfileEmail(email);
        var normalizedProfileImageUrl = NormalizeProfileImageUrl(profileImageUrl);
        var normalizedBio = NormalizeProfileBio(bio);

        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(x => x.UserName == normalizedUserName)
            ?? throw new InvalidOperationException("profileUserNotFound");

        user.DisplayName = normalizedDisplayName;
        user.Email = normalizedEmail;
        user.ProfileImageUrl = normalizedProfileImageUrl;
        user.Bio = normalizedBio;
        user.ProfileUpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        CurrentUser = ToModel(user);
        AuthStateChanged?.Invoke();
        return CurrentUser;
    }

    public async Task<bool> ToggleFollowAsync(string followerUser, string targetUser)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var follower = NormalizeUser(followerUser);
        var target = NormalizeUser(targetUser);
        if (string.IsNullOrWhiteSpace(follower) || string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        if (string.Equals(follower, target, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var knownUsers = await db.Users
            .AsNoTracking()
            .Where(x => x.UserName == follower || x.UserName == target)
            .Select(x => x.UserName)
            .ToListAsync();

        if (!knownUsers.Contains(follower, StringComparer.OrdinalIgnoreCase)
            || !knownUsers.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var existing = await db.Follows.FindAsync(follower, target);
        if (existing is not null)
        {
            db.Follows.Remove(existing);
            await db.SaveChangesAsync();
            return false;
        }

        db.Follows.Add(new FollowRecord
        {
            FollowerUserName = follower,
            TargetUserName = target,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> IsFollowingAsync(string followerUser, string targetUser)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var follower = NormalizeUser(followerUser);
        var target = NormalizeUser(targetUser);
        if (string.IsNullOrWhiteSpace(follower) || string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        return await db.Follows.AsNoTracking().AnyAsync(x => x.FollowerUserName == follower && x.TargetUserName == target);
    }

    public async Task<bool> IsKnownUserAsync(string userName)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = NormalizeUser(userName);
        return !string.IsNullOrWhiteSpace(user)
            && await db.Users.AsNoTracking().AnyAsync(x => x.UserName == user);
    }

    public async Task<AuthUser?> GetUserAsync(string userName)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = NormalizeUser(userName);
        if (string.IsNullOrWhiteSpace(user))
        {
            return null;
        }

        var record = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserName == user);
        return record is null ? null : ToModel(record);
    }

    public async Task<IReadOnlyList<AuthUser>> GetUsersAsync(IEnumerable<string> userNames)
    {
        var normalizedUsers = userNames
            .Select(NormalizeUser)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedUsers.Length == 0)
        {
            return Array.Empty<AuthUser>();
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var records = await db.Users
            .AsNoTracking()
            .Where(x => normalizedUsers.Contains(x.UserName))
            .ToListAsync();

        return records.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<string>> GetFollowingAsync(string followerUser)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var follower = NormalizeUser(followerUser);
        if (string.IsNullOrWhiteSpace(follower))
        {
            return Array.Empty<string>();
        }

        return await db.Follows
            .AsNoTracking()
            .Where(x => x.FollowerUserName == follower)
            .OrderBy(x => x.TargetUserName)
            .Select(x => x.TargetUserName)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<string>> GetFollowersAsync(string targetUser)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var target = NormalizeUser(targetUser);
        if (string.IsNullOrWhiteSpace(target))
        {
            return Array.Empty<string>();
        }

        return await db.Follows
            .AsNoTracking()
            .Where(x => x.TargetUserName == target)
            .OrderBy(x => x.FollowerUserName)
            .Select(x => x.FollowerUserName)
            .ToListAsync();
    }

    public async Task<int> GetFollowerCountAsync(string targetUser)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var target = NormalizeUser(targetUser);
        if (string.IsNullOrWhiteSpace(target))
        {
            return 0;
        }

        return await db.Follows.AsNoTracking().CountAsync(x => x.TargetUserName == target);
    }

    public Task LogoutAsync()
    {
        CurrentUser = null;
        AuthStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public IReadOnlyList<string> GetUserNames()
    {
        using var db = dbFactory.CreateDbContext();
        return db.Users
            .AsNoTracking()
            .OrderBy(x => x.UserName)
            .Select(x => x.UserName)
            .ToList();
    }

    private static AuthUser ToModel(UserRecord record)
    {
        return new AuthUser
        {
            UserName = record.UserName,
            DisplayName = record.DisplayName,
            Email = record.Email,
            Password = record.Password,
            ProfileImageUrl = record.ProfileImageUrl,
            Bio = record.Bio,
            RegisteredAt = record.RegisteredAt
        };
    }

    private static async Task<UserRecord> RecreateExternalUserAsync(
        SlogsDbContext db,
        string userName,
        string email,
        string displayName,
        string profileImageUrl,
        DateTime now)
    {
        var recreatedUser = new UserRecord
        {
            UserName = userName,
            DisplayName = displayName,
            Email = email,
            Password = string.Empty,
            ProfileImageUrl = profileImageUrl,
            Bio = "외부 로그인 계정으로 가입한 슬로거입니다.",
            RegisteredAt = now
        };

        db.Users.Add(recreatedUser);
        await db.SaveChangesAsync();
        return recreatedUser;
    }

    private static void RefreshExternalProfile(UserRecord user, string email, string displayName, string profileImageUrl)
    {
        if (user.ProfileUpdatedAt is not null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            user.Email = email;
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            user.DisplayName = displayName;
        }

        if (!string.IsNullOrWhiteSpace(profileImageUrl))
        {
            user.ProfileImageUrl = profileImageUrl;
        }

        if (string.IsNullOrWhiteSpace(user.Bio))
        {
            user.Bio = "외부 로그인 계정으로 가입한 슬로거입니다.";
        }
    }

    private static async Task<string> CreateUniqueExternalUserNameAsync(
        SlogsDbContext db,
        string provider,
        string email,
        string displayName)
    {
        var baseName = CreateExternalUserNameBase(provider, email, displayName);
        var candidate = baseName;
        var suffix = 2;

        while (await db.Users.AsNoTracking().AnyAsync(x => x.UserName == candidate))
        {
            candidate = $"{baseName}-{suffix++}";
        }

        return candidate;
    }

    private static string CreateExternalUserNameBase(string provider, string email, string displayName)
    {
        var source = email;
        var atIndex = source.IndexOf('@', StringComparison.Ordinal);
        if (atIndex > 0)
        {
            source = source[..atIndex];
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            source = displayName;
        }

        var normalized = new string(source
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray());
        normalized = string.Join('-', normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = provider;
        }

        return normalized.Length <= 50 ? normalized : normalized[..50].Trim('-');
    }

    private static string NormalizeProvider(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string NormalizeDisplayName(string? displayName, string email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var trimmedDisplayName = displayName.Trim();
            return trimmedDisplayName.Length <= 80 ? trimmedDisplayName : trimmedDisplayName[..80];
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var atIndex = email.IndexOf('@', StringComparison.Ordinal);
            var localPart = atIndex > 0 ? email[..atIndex] : email;
            return string.IsNullOrWhiteSpace(localPart) ? "Google 사용자" : localPart;
        }

        return "Google 사용자";
    }

    private static string NormalizeProfileUserName(string? userName, string fallbackUserName)
    {
        var rawUserName = string.IsNullOrWhiteSpace(userName) ? fallbackUserName : userName.Trim();
        if (rawUserName.StartsWith("/@", StringComparison.Ordinal))
        {
            rawUserName = rawUserName[2..];
        }
        else if (rawUserName.StartsWith('@'))
        {
            rawUserName = rawUserName[1..];
        }

        var normalized = NormalizeUser(rawUserName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("profileUserNameRequired");
        }

        if (normalized.Length > 80)
        {
            throw new InvalidOperationException("profileUserNameLength");
        }

        if (!char.IsAsciiLetterOrDigit(normalized[0]) || normalized.Any(character => !IsProfileUserNameCharacter(character)))
        {
            throw new InvalidOperationException("profileUserNameInvalid");
        }

        return normalized;
    }

    private static bool IsProfileUserNameCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.';

    private static string NormalizeProfileDisplayName(string displayName)
    {
        var normalized = displayName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("profileDisplayNameRequired");
        }

        if (normalized.Length > 80)
        {
            throw new InvalidOperationException("profileDisplayNameLength");
        }

        return normalized;
    }

    private static string NormalizeProfileEmail(string? email)
    {
        var normalized = email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Length > 320)
        {
            throw new InvalidOperationException("profileEmailLength");
        }

        try
        {
            var parsed = new MailAddress(normalized);
            if (!parsed.Address.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("profileEmailInvalid");
            }
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("profileEmailInvalid");
        }

        return normalized.ToLowerInvariant();
    }

    private static string NormalizeProfileImageUrl(string? profileImageUrl)
    {
        var normalized = profileImageUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Length > 500)
        {
            throw new InvalidOperationException("profileImageUrlLength");
        }

        if (normalized.StartsWith('/', StringComparison.Ordinal))
        {
            return normalized;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("profileImageUrlInvalid");
        }

        return normalized;
    }

    private static string NormalizeProfileBio(string? bio)
    {
        var normalized = bio?.Trim() ?? string.Empty;
        if (normalized.Length > 280)
        {
            throw new InvalidOperationException("profileBioLength");
        }

        return normalized;
    }

    private static string GetProviderDisplayName(string provider)
        => provider.Equals("google", StringComparison.OrdinalIgnoreCase) ? "Google" : provider;

    private static string NormalizeUser(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
