// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDT.Server.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class BootEnvironmentSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NextEnvironment",
                schema: "ddt",
                table: "Machines",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextEnvironmentUtc",
                schema: "ddt",
                table: "Machines",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingSequenceId",
                schema: "ddt",
                table: "Machines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BootEnvironment",
                schema: "ddt",
                table: "Deployments",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextEnvironment",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "NextEnvironmentUtc",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "PendingSequenceId",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "BootEnvironment",
                schema: "ddt",
                table: "Deployments");
        }
    }
}
