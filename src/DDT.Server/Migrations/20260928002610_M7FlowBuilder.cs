// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDT.Server.Migrations
{
    /// <inheritdoc />
    public partial class M7FlowBuilder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Facts",
                schema: "ddt",
                table: "Machines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Branch",
                schema: "ddt",
                table: "DeploymentSteps",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Depth",
                schema: "ddt",
                table: "DeploymentSteps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Evaluation",
                schema: "ddt",
                table: "DeploymentSteps",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Iteration",
                schema: "ddt",
                table: "DeploymentSteps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                schema: "ddt",
                table: "DeploymentSteps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Pass",
                schema: "ddt",
                table: "DeploymentSteps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Answers",
                schema: "ddt",
                table: "Deployments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContinuePass",
                schema: "ddt",
                table: "Deployments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContinueStepId",
                schema: "ddt",
                table: "Deployments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContinuedByName",
                schema: "ddt",
                table: "Deployments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InputsPending",
                schema: "ddt",
                table: "Deployments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PauseMessage",
                schema: "ddt",
                table: "Deployments",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PausePass",
                schema: "ddt",
                table: "Deployments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PauseStepId",
                schema: "ddt",
                table: "Deployments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Values",
                schema: "ddt",
                table: "Deployments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Variables",
                schema: "ddt",
                table: "Deployments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Accounts",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProtectedPassword = table.Column<string>(type: "text", nullable: true),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    Hosts = table.Column<string>(type: "text", nullable: false),
                    RunAs = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordUpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
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
                name: "MachineRoles",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Values = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
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
                name: "Rules",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    When = table.Column<string>(type: "text", nullable: true),
                    TaskSequenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Values = table.Column<string>(type: "text", nullable: false),
                    RoleIds = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
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
                name: "RunCredentials",
                schema: "ddt",
                columns: table => new
                {
                    DeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InputName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProtectedPassword = table.Column<string>(type: "text", nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    Hosts = table.Column<string>(type: "text", nullable: false),
                    RunAs = table.Column<bool>(type: "boolean", nullable: false),
                    ProvidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProvidedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProvidedAtMachine = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accounts",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "MachineRoles",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "Rules",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "RunCredentials",
                schema: "ddt");

            migrationBuilder.DropColumn(
                name: "Facts",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "Branch",
                schema: "ddt",
                table: "DeploymentSteps");

            migrationBuilder.DropColumn(
                name: "Depth",
                schema: "ddt",
                table: "DeploymentSteps");

            migrationBuilder.DropColumn(
                name: "Evaluation",
                schema: "ddt",
                table: "DeploymentSteps");

            migrationBuilder.DropColumn(
                name: "Iteration",
                schema: "ddt",
                table: "DeploymentSteps");

            migrationBuilder.DropColumn(
                name: "ParentId",
                schema: "ddt",
                table: "DeploymentSteps");

            migrationBuilder.DropColumn(
                name: "Pass",
                schema: "ddt",
                table: "DeploymentSteps");

            migrationBuilder.DropColumn(
                name: "Answers",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "ContinuePass",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "ContinueStepId",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "ContinuedByName",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "InputsPending",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "PauseMessage",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "PausePass",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "PauseStepId",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "Values",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "Variables",
                schema: "ddt",
                table: "Deployments");
        }
    }
}
