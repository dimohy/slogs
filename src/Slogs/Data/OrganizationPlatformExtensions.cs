using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Slogs.Data;

public static class OrganizationPlatformExtensions
{
    public const string OrganizationApiPolicy = "slogs.organization-api";

    public static IServiceCollection AddOrganizationPlatform(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddDbContextFactory<OrganizationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseOpenIddict();
        });

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<OrganizationDbContext>();
            })
            .AddServer(options => ConfigureServer(options, configuration, environment))
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(OrganizationApiPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
            });
        });

        services.AddScoped<OrganizationDirectoryService>();
        services.AddScoped<OrganizationWikiService>();
        services.AddScoped<IOrganizationSemanticIndex, OrganizationSemanticIndex>();
        services.AddScoped<OrganizationTokenService>();
        services.AddScoped<OrganizationMetricsService>();
        services.AddScoped<OrganizationOidcClientService>();
        services.AddScoped<OrganizationGuidedAccessService>();
        services.AddScoped<OrganizationActorResolver>();

        return services;
    }

    private static void ConfigureServer(
        OpenIddictServerBuilder options,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        options.SetAuthorizationEndpointUris("/connect/authorize")
            .SetTokenEndpointUris("/connect/token")
            .SetEndSessionEndpointUris("/connect/logout");
        options.AllowAuthorizationCodeFlow();
        options.RequireProofKeyForCodeExchange();
        options.Configure(serverOptions =>
            serverOptions.CodeChallengeMethods.Remove(OpenIddictConstants.CodeChallengeMethods.Plain));
        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Roles,
            OrganizationTokenScopes.Read,
            OrganizationTokenScopes.Propose,
            OrganizationTokenScopes.Approve,
            OrganizationTokenScopes.Reject,
            OrganizationTokenScopes.MembersManage,
            OrganizationTokenScopes.SourcesManage,
            OrganizationTokenScopes.McpManage,
            OrganizationTokenScopes.OidcManage,
            OrganizationTokenScopes.MetricsRead,
            OrganizationTokenScopes.MetricsWrite,
            OrganizationTokenScopes.GuidedSession);

        if (environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate()
                .AddDevelopmentSigningCertificate();
        }
        else
        {
            options.AddEncryptionCertificate(LoadCertificate(configuration, "Encryption"));
            options.AddSigningCertificate(LoadCertificate(configuration, "Signing"));
        }

        options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough();
    }

    private static X509Certificate2 LoadCertificate(IConfiguration configuration, string purpose)
    {
        var path = configuration[$"Authentication:OpenIddict:{purpose}CertificatePath"];
        var password = configuration[$"Authentication:OpenIddict:{purpose}CertificatePassword"];
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"Authentication:OpenIddict:{purpose}CertificatePath and {purpose}CertificatePassword are required in production.");
        }

        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The configured OpenIddict {purpose.ToLowerInvariant()} certificate path must be an existing absolute file path.");
        }

        return X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet);
    }
}
