using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Slogs.Data.OrganizationMigrations
{
    /// <inheritdoc />
    public partial class _20260825022002_OrganizationPlatformInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organization");

            migrationBuilder.CreateTable(
                name: "OpenIddictApplications",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClientSecret = table.Column<string>(type: "text", nullable: true),
                    ClientType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConsentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    JsonWebKeySet = table.Column<string>(type: "text", nullable: true),
                    Permissions = table.Column<string>(type: "text", nullable: true),
                    PostLogoutRedirectUris = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedirectUris = table.Column<string>(type: "text", nullable: true),
                    Requirements = table.Column<string>(type: "text", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictScopes",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Descriptions = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictScopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationAudits",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PresenterUserName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DetailJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationConflicts",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LeftValue = table.Column<string>(type: "text", nullable: false),
                    RightValue = table.Column<string>(type: "text", nullable: false),
                    LeftMemoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    RightMemoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeftSourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    RightSourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationConflicts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationGuidedSessions",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresenterUserName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ActiveRoleUserName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationGuidedSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMetricEvents",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MetricKind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    IsDemoAssumption = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMetricEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationOidcClients",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RedirectUrisJson = table.Column<string>(type: "jsonb", nullable: false),
                    ScopesJson = table.Column<string>(type: "jsonb", nullable: false),
                    SecretVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationOidcClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EnvironmentLabel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MinimumAggregateCohort = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationServiceTokens",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenPrefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScopesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationServiceTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictAuthorizations",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Scopes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenIddictAuthorizations_OpenIddictApplications_Application~",
                        column: x => x.ApplicationId,
                        principalSchema: "organization",
                        principalTable: "OpenIddictApplications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMemberships",
                schema: "organization",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayRole = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsSyntheticAccount = table.Column<bool>(type: "boolean", nullable: false),
                    InvitedBy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemberships", x => new { x.OrganizationId, x.UserName });
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMemories",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SourcePrompt = table.Column<string>(type: "text", nullable: false),
                    TagsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CategoryPath = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CategoryDepth = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScopeKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    ProposedBy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SupersedesMemoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMemories_OrganizationMemories_SupersedesMemoryId",
                        column: x => x.SupersedesMemoryId,
                        principalSchema: "organization",
                        principalTable: "OrganizationMemories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationMemories_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationUnits",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NameKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParentUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationUnits_OrganizationUnits_ParentUnitId",
                        column: x => x.ParentUnitId,
                        principalSchema: "organization",
                        principalTable: "OrganizationUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationUnits_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictTokens",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    AuthorizationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedemptionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenIddictTokens_OpenIddictApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "organization",
                        principalTable: "OpenIddictApplications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId",
                        column: x => x.AuthorizationId,
                        principalSchema: "organization",
                        principalTable: "OpenIddictAuthorizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMemoryRevisions",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MemoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorUserName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PresenterUserName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SourcePrompt = table.Column<string>(type: "text", nullable: false),
                    TagsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CategoryPath = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScopeKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemoryRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMemoryRevisions_OrganizationMemories_MemoryId",
                        column: x => x.MemoryId,
                        principalSchema: "organization",
                        principalTable: "OrganizationMemories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMemorySources",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceUri = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Grade = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Excerpt = table.Column<string>(type: "text", nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemorySources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMemorySources_OrganizationMemories_MemoryId",
                        column: x => x.MemoryId,
                        principalSchema: "organization",
                        principalTable: "OrganizationMemories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationUnitMemberships",
                schema: "organization",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationUnitMemberships", x => new { x.OrganizationId, x.UnitId, x.UserName });
                    table.ForeignKey(
                        name: "FK_OrganizationUnitMemberships_OrganizationUnits_UnitId",
                        column: x => x.UnitId,
                        principalSchema: "organization",
                        principalTable: "OrganizationUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictApplications_ClientId",
                schema: "organization",
                table: "OpenIddictApplications",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictAuthorizations_ApplicationId_Status_Subject_Type",
                schema: "organization",
                table: "OpenIddictAuthorizations",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictScopes_Name",
                schema: "organization",
                table: "OpenIddictScopes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictTokens_ApplicationId_Status_Subject_Type",
                schema: "organization",
                table: "OpenIddictTokens",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictTokens_AuthorizationId",
                schema: "organization",
                table: "OpenIddictTokens",
                column: "AuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictTokens_ReferenceId",
                schema: "organization",
                table: "OpenIddictTokens",
                column: "ReferenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAudits_OrganizationId_ActorId_CreatedAt",
                schema: "organization",
                table: "OrganizationAudits",
                columns: new[] { "OrganizationId", "ActorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAudits_OrganizationId_CreatedAt",
                schema: "organization",
                table: "OrganizationAudits",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConflicts_OrganizationId_State_CreatedAt",
                schema: "organization",
                table: "OrganizationConflicts",
                columns: new[] { "OrganizationId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationGuidedSessions_OrganizationId_PresenterUserName~",
                schema: "organization",
                table: "OrganizationGuidedSessions",
                columns: new[] { "OrganizationId", "PresenterUserName", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_UserName_Status",
                schema: "organization",
                table: "OrganizationMemberships",
                columns: new[] { "UserName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemories_OrganizationId_CategoryPath_UpdatedAt",
                schema: "organization",
                table: "OrganizationMemories",
                columns: new[] { "OrganizationId", "CategoryPath", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemories_OrganizationId_ScopeKind_ScopeKey_Upda~",
                schema: "organization",
                table: "OrganizationMemories",
                columns: new[] { "OrganizationId", "ScopeKind", "ScopeKey", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemories_OrganizationId_Slug",
                schema: "organization",
                table: "OrganizationMemories",
                columns: new[] { "OrganizationId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemories_OrganizationId_State_UpdatedAt",
                schema: "organization",
                table: "OrganizationMemories",
                columns: new[] { "OrganizationId", "State", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemories_SupersedesMemoryId",
                schema: "organization",
                table: "OrganizationMemories",
                column: "SupersedesMemoryId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemoryRevisions_MemoryId_Revision",
                schema: "organization",
                table: "OrganizationMemoryRevisions",
                columns: new[] { "MemoryId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemorySources_MemoryId",
                schema: "organization",
                table: "OrganizationMemorySources",
                column: "MemoryId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemorySources_OrganizationId_SourceUri_ContentH~",
                schema: "organization",
                table: "OrganizationMemorySources",
                columns: new[] { "OrganizationId", "SourceUri", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemorySources_OrganizationId_State_LastCheckedAt",
                schema: "organization",
                table: "OrganizationMemorySources",
                columns: new[] { "OrganizationId", "State", "LastCheckedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMetricEvents_OrganizationId_UnitId_MetricKind_O~",
                schema: "organization",
                table: "OrganizationMetricEvents",
                columns: new[] { "OrganizationId", "UnitId", "MetricKind", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationOidcClients_ClientId",
                schema: "organization",
                table: "OrganizationOidcClients",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationOidcClients_OrganizationId_RevokedAt",
                schema: "organization",
                table: "OrganizationOidcClients",
                columns: new[] { "OrganizationId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Slug",
                schema: "organization",
                table: "Organizations",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationServiceTokens_OrganizationId_RevokedAt",
                schema: "organization",
                table: "OrganizationServiceTokens",
                columns: new[] { "OrganizationId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationServiceTokens_TokenHash",
                schema: "organization",
                table: "OrganizationServiceTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnitMemberships_OrganizationId_UserName",
                schema: "organization",
                table: "OrganizationUnitMemberships",
                columns: new[] { "OrganizationId", "UserName" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnitMemberships_UnitId",
                schema: "organization",
                table: "OrganizationUnitMemberships",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnits_OrganizationId_ParentUnitId_NameKey",
                schema: "organization",
                table: "OrganizationUnits",
                columns: new[] { "OrganizationId", "ParentUnitId", "NameKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnits_ParentUnitId",
                schema: "organization",
                table: "OrganizationUnits",
                column: "ParentUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenIddictScopes",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OpenIddictTokens",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OrganizationAudits",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OrganizationConflicts",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OrganizationGuidedSessions",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OrganizationMemberships",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OrganizationMemoryRevisions",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OrganizationMemorySources",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OrganizationMetricEvents",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OrganizationOidcClients",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OrganizationServiceTokens",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OrganizationUnitMemberships",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OpenIddictAuthorizations",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OrganizationMemories",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OrganizationUnits",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OpenIddictApplications",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Organizations",
                schema: "organization");
        }
    }
}
