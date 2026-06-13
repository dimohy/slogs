using Microsoft.EntityFrameworkCore;

namespace Slogs.Data;

public sealed class AuthService(IDbContextFactory<SlogsDbContext> dbFactory)
{
    private readonly object stateLock = new();
    private AuthUser? currentUser;

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

        if (await db.Users.AnyAsync(x => x.UserName == normalized))
        {
            throw new InvalidOperationException("이미 사용 중인 아이디입니다.");
        }

        var newUser = new UserRecord
        {
            UserName = normalized,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName.Trim() : displayName.Trim(),
            Password = password,
            ProfileImageUrl = GetDefaultProfileImageUrl(normalized),
            Bio = "slogs에서 새 글을 준비 중인 작성자입니다.",
            RegisteredAt = DateTime.UtcNow
        };

        db.Users.Add(newUser);
        await db.SaveChangesAsync();

        CurrentUser = ToModel(newUser);
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
            Password = record.Password,
            ProfileImageUrl = record.ProfileImageUrl,
            Bio = record.Bio,
            RegisteredAt = record.RegisteredAt
        };
    }

    private static string NormalizeUser(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string GetDefaultProfileImageUrl(string userName)
    {
        var seed = Uri.EscapeDataString(string.IsNullOrWhiteSpace(userName) ? "slogs" : userName.Trim());
        return $"https://api.dicebear.com/9.x/initials/svg?seed={seed}&backgroundColor=e0f2fe,dbeafe,f0fdf4,fef3c7";
    }
}
