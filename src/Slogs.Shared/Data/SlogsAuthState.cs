namespace Slogs.Data;

public sealed class SlogsAuthState(SlogsApiClient apiClient)
{
    private bool isInitialized;
    private AuthUser? currentUser;

    public bool IsInitialized => isInitialized;
    public AuthUser? CurrentUser => currentUser;

    public event Action? AuthStateChanged;

    public async Task<AuthUser?> EnsureInitializedAsync()
    {
        if (!isInitialized)
        {
            await RefreshAsync();
        }

        return currentUser;
    }

    public async Task<AuthUser?> RefreshAsync()
    {
        currentUser = await apiClient.GetCurrentUserAsync();
        isInitialized = true;
        AuthStateChanged?.Invoke();
        return currentUser;
    }

    public void Restore(AuthUser? user)
    {
        currentUser = user;
        isInitialized = true;
        AuthStateChanged?.Invoke();
    }

    public Task LogoutAsync()
    {
        currentUser = null;
        isInitialized = true;
        AuthStateChanged?.Invoke();
        return Task.CompletedTask;
    }
}
