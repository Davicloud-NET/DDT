// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDT.Server.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class StepDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentStepDetail",
                schema: "ddt",
                table: "Deployments",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentStepDetail",
                schema: "ddt",
                table: "Deployments");
        }
    }
}
