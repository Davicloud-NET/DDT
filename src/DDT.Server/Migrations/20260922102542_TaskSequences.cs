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
            // EF takes the M4 columns this migration drops for renamed ones (WimIndex, Step, ImageId). Clearing them first
            // makes each rename a drop and an add.
            migrationBuilder.Sql("""
                UPDATE ddt."Deployments" SET "WimIndex" = 0, "Step" = NULL, "ImageId" = NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Deployments_Images_ImageId",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "InstalledBytes",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "Sha256",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.RenameColumn(
                name: "WimIndex",
                schema: "ddt",
                table: "Deployments",
                newName: "StepCount");

            migrationBuilder.RenameColumn(
                name: "Step",
                schema: "ddt",
                table: "Deployments",
                newName: "CurrentPhase");

            migrationBuilder.RenameColumn(
                name: "ImageName",
                schema: "ddt",
                table: "Deployments",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "ImageId",
                schema: "ddt",
                table: "Deployments",
                newName: "TaskSequenceId");

            migrationBuilder.RenameIndex(
                name: "IX_Deployments_ImageId",
                schema: "ddt",
                table: "Deployments",
                newName: "IX_Deployments_TaskSequenceId");

            migrationBuilder.AddColumn<Guid>(
                name: "LastDeploymentId",
                schema: "ddt",
                table: "Machines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "ddt",
                table: "ImageUploads",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Image");

            migrationBuilder.AddColumn<string>(
                name: "Activity",
                schema: "ddt",
                table: "Deployments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentStepIndex",
                schema: "ddt",
                table: "Deployments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentStepName",
                schema: "ddt",
                table: "Deployments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Inputs",
                schema: "ddt",
                table: "Deployments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RuleId",
                schema: "ddt",
                table: "Deployments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SequenceRevision",
                schema: "ddt",
                table: "Deployments",
                type: "bigint",
                nullable: true);

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
                name: "DeploymentSnapshots",
                schema: "ddt",
                columns: table => new
                {
                    DeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Definition = table.Column<string>(type: "text", nullable: false)
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
                    DeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Phase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Percent = table.Column<int>(type: "integer", nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "AssignmentRules",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MatchKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Mac = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    Manufacturer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TaskSequenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentRules_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssignmentRules_TaskSequences_TaskSequenceId",
                        column: x => x.TaskSequenceId,
                        principalSchema: "ddt",
                        principalTable: "TaskSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentRules_MatchKey",
                schema: "ddt",
                table: "AssignmentRules",
                column: "MatchKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentRules_TaskSequenceId",
                schema: "ddt",
                table: "AssignmentRules",
                column: "TaskSequenceId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentRules_UpdatedByUserId",
                schema: "ddt",
                table: "AssignmentRules",
                column: "UpdatedByUserId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_Deployments_TaskSequences_TaskSequenceId",
                schema: "ddt",
                table: "Deployments",
                column: "TaskSequenceId",
                principalSchema: "ddt",
                principalTable: "TaskSequences",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Image deployments cannot continue as task sequences: the agents that ran them are replaced at their
            // next netboot. The machines keep their approval, but every token they hold dies with the generation.
            migrationBuilder.Sql("""
                UPDATE ddt."Deployments"
                SET "State" = 'Cancelled',
                    "Error" = 'DDT was upgraded to task sequences before this image was installed. Assign a task sequence instead.',
                    "FinishedUtc" = now(),
                    "UpdatedUtc" = now()
                WHERE "State" = 'Assigned';

                UPDATE ddt."Deployments"
                SET "State" = 'Failed',
                    "Error" = 'DDT was upgraded to task sequences while this image was being installed. Assign a task sequence to install the machine again.',
                    "FinishedUtc" = now(),
                    "UpdatedUtc" = now()
                WHERE "State" = 'Running';

                UPDATE ddt."Machines" SET "State" = 'Failed' WHERE "State" = 'Deploying';

                UPDATE ddt."Machines"
                SET "TokenGeneration" = "TokenGeneration" + 1, "ActiveDeploymentId" = NULL
                WHERE "ActiveDeploymentId" IS NOT NULL;

                UPDATE ddt."Machines" AS m
                SET "LastDeploymentId" = (
                    SELECT d."Id" FROM ddt."Deployments" AS d
                    WHERE d."MachineId" = m."Id"
                    ORDER BY d."CreatedUtc" DESC, d."Id" DESC
                    LIMIT 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The columns EF renames back keep only the values the M4 schema can read.
            migrationBuilder.Sql("""
                UPDATE ddt."Deployments" SET "StepCount" = 0, "CurrentPhase" = NULL, "TaskSequenceId" = NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Deployments_TaskSequences_TaskSequenceId",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropTable(
                name: "AssignmentRules",
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
                name: "Packages",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "TaskSequences",
                schema: "ddt");

            migrationBuilder.DropColumn(
                name: "LastDeploymentId",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "ddt",
                table: "ImageUploads");

            migrationBuilder.DropColumn(
                name: "Activity",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "CurrentStepIndex",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "CurrentStepName",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "Inputs",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "RuleId",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "SequenceRevision",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.RenameColumn(
                name: "Title",
                schema: "ddt",
                table: "Deployments",
                newName: "ImageName");

            migrationBuilder.RenameColumn(
                name: "TaskSequenceId",
                schema: "ddt",
                table: "Deployments",
                newName: "ImageId");

            migrationBuilder.RenameColumn(
                name: "StepCount",
                schema: "ddt",
                table: "Deployments",
                newName: "WimIndex");

            migrationBuilder.RenameColumn(
                name: "CurrentPhase",
                schema: "ddt",
                table: "Deployments",
                newName: "Step");

            migrationBuilder.RenameIndex(
                name: "IX_Deployments_TaskSequenceId",
                schema: "ddt",
                table: "Deployments",
                newName: "IX_Deployments_ImageId");

            migrationBuilder.AddColumn<long>(
                name: "InstalledBytes",
                schema: "ddt",
                table: "Deployments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                schema: "ddt",
                table: "Deployments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                schema: "ddt",
                table: "Deployments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddForeignKey(
                name: "FK_Deployments_Images_ImageId",
                schema: "ddt",
                table: "Deployments",
                column: "ImageId",
                principalSchema: "ddt",
                principalTable: "Images",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
