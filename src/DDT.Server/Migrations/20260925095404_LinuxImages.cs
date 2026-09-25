// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDT.Server.Migrations
{
    /// <inheritdoc />
    public partial class LinuxImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SecureBootEnabled",
                schema: "ddt",
                table: "Machines",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BootCapability",
                schema: "ddt",
                table: "Images",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BootDetail",
                schema: "ddt",
                table: "Images",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSha256",
                schema: "ddt",
                table: "Images",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowSecureBootMismatch",
                schema: "ddt",
                table: "Deployments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BootCapability",
                schema: "ddt",
                table: "DeploymentArtifacts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Images_SourceSha256",
                schema: "ddt",
                table: "Images",
                column: "SourceSha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Images_SourceSha256",
                schema: "ddt",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "SecureBootEnabled",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "BootCapability",
                schema: "ddt",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "BootDetail",
                schema: "ddt",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "SourceSha256",
                schema: "ddt",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "AllowSecureBootMismatch",
                schema: "ddt",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "BootCapability",
                schema: "ddt",
                table: "DeploymentArtifacts");
        }
    }
}
