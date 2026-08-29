using System.Net.Http.Json;

namespace Slogs.Data;

public sealed class OrganizationApiClient(HttpClient httpClient)
{
    public Task<IReadOnlyList<OrganizationResponse>> GetMyOrganizationsAsync(CancellationToken cancellationToken = default)
        => GetListAsync<OrganizationResponse>("api/organizations/me", cancellationToken);

    public Task<IReadOnlyList<OrganizationResponse>> GetAllOrganizationsAsync(CancellationToken cancellationToken = default)
        => GetListAsync<OrganizationResponse>("api/organizations/all", cancellationToken);

    public async Task<OrganizationResponse> CreateOrganizationAsync(
        OrganizationCreateRequest request,
        CancellationToken cancellationToken = default)
        => await SendAsync<OrganizationCreateRequest, OrganizationResponse>(
            HttpMethod.Post,
            "api/organizations/",
            request,
            cancellationToken);

    public Task<IReadOnlyList<OrganizationMembershipResponse>> GetMembersAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
        => GetListAsync<OrganizationMembershipResponse>($"api/organizations/{organizationId}/members", cancellationToken);

    public async Task<OrganizationMembershipResponse> UpsertMemberAsync(
        Guid organizationId,
        OrganizationMembershipUpsertRequest request,
        CancellationToken cancellationToken = default)
        => await SendAsync<OrganizationMembershipUpsertRequest, OrganizationMembershipResponse>(
            HttpMethod.Put,
            $"api/organizations/{organizationId}/members",
            request,
            cancellationToken);

    public Task<IReadOnlyList<OrganizationOidcClientResponse>> GetOidcClientsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
        => GetListAsync<OrganizationOidcClientResponse>($"api/organizations/{organizationId}/oidc-clients", cancellationToken);

    public async Task<OrganizationOidcClientCreatedResponse> CreateOidcClientAsync(
        Guid organizationId,
        OrganizationOidcClientCreateRequest request,
        CancellationToken cancellationToken = default)
        => await SendAsync<OrganizationOidcClientCreateRequest, OrganizationOidcClientCreatedResponse>(
            HttpMethod.Post,
            $"api/organizations/{organizationId}/oidc-clients",
            request,
            cancellationToken);

    public async Task<OrganizationOidcClientCreatedResponse> RotateOidcSecretAsync(
        Guid organizationId,
        Guid clientId,
        CancellationToken cancellationToken = default)
        => await SendAsync<OrganizationOidcClientCreatedResponse>(
            HttpMethod.Post,
            $"api/organizations/{organizationId}/oidc-clients/{clientId}/rotate-secret",
            cancellationToken);

    public async Task RevokeOidcClientAsync(
        Guid organizationId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync(
            $"api/organizations/{organizationId}/oidc-clients/{clientId}",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<OrganizationServiceTokenResponse>> GetServiceTokensAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
        => GetListAsync<OrganizationServiceTokenResponse>(
            $"api/organizations/{organizationId}/service-tokens",
            cancellationToken);

    public async Task<OrganizationServiceTokenCreatedResponse> CreateServiceTokenAsync(
        Guid organizationId,
        OrganizationServiceTokenCreateRequest request,
        CancellationToken cancellationToken = default)
        => await SendAsync<OrganizationServiceTokenCreateRequest, OrganizationServiceTokenCreatedResponse>(
            HttpMethod.Post,
            $"api/organizations/{organizationId}/service-tokens",
            request,
            cancellationToken);

    public async Task RevokeServiceTokenAsync(
        Guid organizationId,
        Guid tokenId,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync(
            $"api/organizations/{organizationId}/service-tokens/{tokenId}",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken)
            ?? [];
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string uri,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(request)
        };
        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Organization API returned an empty response.");
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string uri,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, uri);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Organization API returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken);
        throw new InvalidOperationException(error?.Error ?? $"Organization API failed with HTTP {(int)response.StatusCode}.");
    }
}
