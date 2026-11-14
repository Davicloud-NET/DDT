// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDT.Server.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class WindowsPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Architecture",
                schema: "ddt",
                table: "Packages",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Content",
                schema: "ddt",
                table: "Packages",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KbNumbers",
                schema: "ddt",
                table: "Packages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Languages",
                schema: "ddt",
                table: "Packages",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Architecture",
                schema: "ddt",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Content",
                schema: "ddt",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "KbNumbers",
                schema: "ddt",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Languages",
                schema: "ddt",
                table: "Packages");
        }
    }
}
