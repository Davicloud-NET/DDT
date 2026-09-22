// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DDT.Server.Migrations
{
    /// <inheritdoc />
    public partial class TaskSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "ddt",
                table: "ImageUploads",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Image");

            migrationBuilder.CreateTable(
                name: "DeploymentArtifacts",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ExpandedBytes = table.Column<long>(type: "bigint", nullable: false),
                    WimIndex = table.Column<int>(type: "integer", nullable: true),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
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
                name: "Packages",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ExpandedBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileCount = table.Column<int>(type: "integer", nullable: false),
                    Targets = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UploadedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
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
                name: "TaskSequences",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Definition = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
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
                name: "DeploymentArtifacts",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "Packages",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "TaskSequences",
                schema: "ddt");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "ddt",
                table: "ImageUploads");
        }
    }
}
