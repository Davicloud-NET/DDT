// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDT.Server.Migrations
{
    /// <inheritdoc />
    public partial class MachineSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SignedInByUserId",
                schema: "ddt",
                table: "Machines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedInUserName",
                schema: "ddt",
                table: "Machines",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignedInUtc",
                schema: "ddt",
                table: "Machines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Machines_SignedInByUserId",
                schema: "ddt",
                table: "Machines",
                column: "SignedInByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Machines_AspNetUsers_SignedInByUserId",
                schema: "ddt",
                table: "Machines",
                column: "SignedInByUserId",
                principalSchema: "ddt",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Machines_AspNetUsers_SignedInByUserId",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropIndex(
                name: "IX_Machines_SignedInByUserId",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "SignedInByUserId",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "SignedInUserName",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "SignedInUtc",
                schema: "ddt",
                table: "Machines");
        }
    }
}
