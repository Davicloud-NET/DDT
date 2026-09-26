// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDT.Server.Migrations
{
    /// <inheritdoc />
    public partial class MachineChassisType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChassisType",
                schema: "ddt",
                table: "Machines",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChassisType",
                schema: "ddt",
                table: "Machines");
        }
    }
}
