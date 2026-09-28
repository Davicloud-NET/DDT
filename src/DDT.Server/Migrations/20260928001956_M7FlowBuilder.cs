// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Facts",
                schema: "ddt",
                table: "Machines");
        }
    }
}
