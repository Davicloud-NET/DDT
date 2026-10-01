// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDT.Server.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ddt");

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DirectoryObjectId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSignInUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorMachineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorTokenId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SubjectId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SettingsHostStates",
                schema: "ddt",
                columns: table => new
                {
                    Host = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Section = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AppliedVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingsHostStates", x => new { x.Host, x.Section });
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "ddt",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Accounts",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ProtectedPassword = table.Column<string>(type: "TEXT", nullable: true),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    Hosts = table.Column<string>(type: "TEXT", nullable: false),
                    RunAs = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordUpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ApiTokens",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SecretHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Hint = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastUsedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastUsedAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RevokedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RevokedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiTokens_AspNetUsers_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ApiTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "ddt",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserPasskeys",
                schema: "ddt",
                columns: table => new
                {
                    CredentialId = table.Column<byte[]>(type: "BLOB", maxLength: 1024, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserPasskeys", x => x.CredentialId);
                    table.ForeignKey(
                        name: "FK_AspNetUserPasskeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "ddt",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "ddt",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "ddt",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Images",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    WimIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Edition = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Architecture = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    InstalledBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    UploadedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UploadedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    BootCapability = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    SignedUnder = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    BootDetail = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SourceSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Images_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ImageUploads",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Length = table.Column<long>(type: "INTEGER", nullable: false),
                    LastModified = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, defaultValue: "Image"),
                    Offset = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageUploads_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MachineRoles",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Values = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineRoles_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Machines",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SmbiosUuid = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PrimaryMac = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MacAddresses = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AssignedName = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    AgentVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SequenceVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentEnvironment = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, defaultValue: "WindowsPE"),
                    SecureBootEnabled = table.Column<bool>(type: "INTEGER", nullable: true),
                    TrustedUefiCas = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ChassisType = table.Column<int>(type: "INTEGER", nullable: true),
                    Facts = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    TokenGeneration = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeenUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FirstSeenAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastSeenAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FirstApprovedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SignedInByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SignedInUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SignedInUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Disks = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    EligibleDiskCount = table.Column<int>(type: "INTEGER", nullable: true),
                    ActiveDeploymentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastDeploymentId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Machines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Machines_AspNetUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Machines_AspNetUsers_SignedInByUserId",
                        column: x => x.SignedInByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Packages",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpandedBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Targets = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    UploadedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BootImage = table.Column<bool>(type: "INTEGER", nullable: false),
                    UploadedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Packages_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SettingsSections",
                schema: "ddt",
                columns: table => new
                {
                    Section = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Values = table.Column<string>(type: "TEXT", nullable: false),
                    Secrets = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingsSections", x => x.Section);
                    table.ForeignKey(
                        name: "FK_SettingsSections_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TaskSequences",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Definition = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskSequences_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MachineLogLines",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MachineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AgentTimestampUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReceivedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    DeploymentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StepId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineLogLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineLogLines_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "ddt",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Deployments",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MachineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskSequenceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SequenceRevision = table.Column<long>(type: "INTEGER", nullable: true),
                    RuleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DiskNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    StepCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentStepIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    CurrentStepName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Percent = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentPhase = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Activity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Inputs = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinishedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    AllowSecureBootMismatch = table.Column<bool>(type: "INTEGER", nullable: false),
                    Answers = table.Column<string>(type: "TEXT", nullable: true),
                    Values = table.Column<string>(type: "TEXT", nullable: true),
                    Variables = table.Column<string>(type: "TEXT", nullable: true),
                    InputsPending = table.Column<bool>(type: "INTEGER", nullable: false),
                    PauseStepId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PausePass = table.Column<int>(type: "INTEGER", nullable: true),
                    PauseMessage = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ContinueStepId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContinuePass = table.Column<int>(type: "INTEGER", nullable: true),
                    ContinuedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deployments_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Deployments_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "ddt",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Deployments_TaskSequences_TaskSequenceId",
                        column: x => x.TaskSequenceId,
                        principalSchema: "ddt",
                        principalTable: "TaskSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Rules",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    When = table.Column<string>(type: "TEXT", nullable: true),
                    TaskSequenceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Values = table.Column<string>(type: "TEXT", nullable: false),
                    RoleIds = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rules_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Rules_TaskSequences_TaskSequenceId",
                        column: x => x.TaskSequenceId,
                        principalSchema: "ddt",
                        principalTable: "TaskSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentArtifacts",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeploymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpandedBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    WimIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    BootCapability = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    SignedUnder = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentArtifacts_Deployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalSchema: "ddt",
                        principalTable: "Deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentSnapshots",
                schema: "ddt",
                columns: table => new
                {
                    DeploymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Definition = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentSnapshots", x => x.DeploymentId);
                    table.ForeignKey(
                        name: "FK_DeploymentSnapshots_Deployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalSchema: "ddt",
                        principalTable: "Deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentSteps",
                schema: "ddt",
                columns: table => new
                {
                    DeploymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Percent = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinishedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    Pass = table.Column<int>(type: "INTEGER", nullable: false),
                    Iteration = table.Column<int>(type: "INTEGER", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Evaluation = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentSteps", x => new { x.DeploymentId, x.StepId });
                    table.ForeignKey(
                        name: "FK_DeploymentSteps_Deployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalSchema: "ddt",
                        principalTable: "Deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunCredentials",
                schema: "ddt",
                columns: table => new
                {
                    DeploymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InputName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ProtectedPassword = table.Column<string>(type: "TEXT", nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    Hosts = table.Column<string>(type: "TEXT", nullable: false),
                    RunAs = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProvidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProvidedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ProvidedAtMachine = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunCredentials", x => new { x.DeploymentId, x.InputName });
                    table.ForeignKey(
                        name: "FK_RunCredentials_AspNetUsers_ProvidedByUserId",
                        column: x => x.ProvidedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunCredentials_Deployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalSchema: "ddt",
                        principalTable: "Deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_NormalizedName",
                schema: "ddt",
                table: "Accounts",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UpdatedByUserId",
                schema: "ddt",
                table: "Accounts",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_RevokedByUserId",
                schema: "ddt",
                table: "ApiTokens",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_SecretHash",
                schema: "ddt",
                table: "ApiTokens",
                column: "SecretHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_UserId",
                schema: "ddt",
                table: "ApiTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "ddt",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "ddt",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "ddt",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "ddt",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserPasskeys_UserId",
                schema: "ddt",
                table: "AspNetUserPasskeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "ddt",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "ddt",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DirectoryObjectId",
                schema: "ddt",
                table: "AspNetUsers",
                column: "DirectoryObjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "ddt",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Action",
                schema: "ddt",
                table: "AuditEvents",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredUtc",
                schema: "ddt",
                table: "AuditEvents",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentArtifacts_DeploymentId",
                schema: "ddt",
                table: "DeploymentArtifacts",
                column: "DeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentArtifacts_Sha256",
                schema: "ddt",
                table: "DeploymentArtifacts",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentArtifacts_SourceId",
                schema: "ddt",
                table: "DeploymentArtifacts",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_MachineId",
                schema: "ddt",
                table: "Deployments",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_RequestedByUserId",
                schema: "ddt",
                table: "Deployments",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_TaskSequenceId",
                schema: "ddt",
                table: "Deployments",
                column: "TaskSequenceId");

            migrationBuilder.CreateIndex(
                name: "IX_Images_Sha256",
                schema: "ddt",
                table: "Images",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_Images_SourceSha256",
                schema: "ddt",
                table: "Images",
                column: "SourceSha256");

            migrationBuilder.CreateIndex(
                name: "IX_Images_UploadedByUserId",
                schema: "ddt",
                table: "Images",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageUploads_CreatedByUserId",
                schema: "ddt",
                table: "ImageUploads",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageUploads_FileName_Length_LastModified",
                schema: "ddt",
                table: "ImageUploads",
                columns: new[] { "FileName", "Length", "LastModified" });

            migrationBuilder.CreateIndex(
                name: "IX_MachineLogLines_DeploymentId_Id",
                schema: "ddt",
                table: "MachineLogLines",
                columns: new[] { "DeploymentId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MachineLogLines_MachineId_Id",
                schema: "ddt",
                table: "MachineLogLines",
                columns: new[] { "MachineId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MachineRoles_NormalizedName",
                schema: "ddt",
                table: "MachineRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineRoles_UpdatedByUserId",
                schema: "ddt",
                table: "MachineRoles",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Machines_ApprovedByUserId",
                schema: "ddt",
                table: "Machines",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Machines_FirstSeenAddress",
                schema: "ddt",
                table: "Machines",
                column: "FirstSeenAddress");

            migrationBuilder.CreateIndex(
                name: "IX_Machines_PrimaryMac",
                schema: "ddt",
                table: "Machines",
                column: "PrimaryMac");

            migrationBuilder.CreateIndex(
                name: "IX_Machines_SignedInByUserId",
                schema: "ddt",
                table: "Machines",
                column: "SignedInByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Machines_SmbiosUuid",
                schema: "ddt",
                table: "Machines",
                column: "SmbiosUuid");

            migrationBuilder.CreateIndex(
                name: "IX_Packages_Sha256",
                schema: "ddt",
                table: "Packages",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_Packages_UploadedByUserId",
                schema: "ddt",
                table: "Packages",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Rules_Position",
                schema: "ddt",
                table: "Rules",
                column: "Position",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rules_TaskSequenceId",
                schema: "ddt",
                table: "Rules",
                column: "TaskSequenceId");

            migrationBuilder.CreateIndex(
                name: "IX_Rules_UpdatedByUserId",
                schema: "ddt",
                table: "Rules",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RunCredentials_ProvidedByUserId",
                schema: "ddt",
                table: "RunCredentials",
                column: "ProvidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SettingsSections_UpdatedByUserId",
                schema: "ddt",
                table: "SettingsSections",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskSequences_NormalizedName",
                schema: "ddt",
                table: "TaskSequences",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskSequences_UpdatedByUserId",
                schema: "ddt",
                table: "TaskSequences",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accounts",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "ApiTokens",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "AspNetUserPasskeys",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "AuditEvents",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "DeploymentArtifacts",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "DeploymentSnapshots",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "DeploymentSteps",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "Images",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "ImageUploads",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "MachineLogLines",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "MachineRoles",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "Packages",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "Rules",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "RunCredentials",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "SettingsHostStates",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "SettingsSections",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "Deployments",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "Machines",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "TaskSequences",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "ddt");
        }
    }
}
