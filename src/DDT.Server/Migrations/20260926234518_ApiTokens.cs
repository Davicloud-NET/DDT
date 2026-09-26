// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDT.Server.Migrations
{
    /// <inheritdoc />
    public partial class ApiTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActorTokenId",
                schema: "ddt",
                table: "AuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApiTokens",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Hint = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RevokedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiTokens_AspNetUsers_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ApiTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_RevokedByUserId",
                schema: "ddt",
                table: "ApiTokens",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_SecretHash",
                schema: "ddt",
                table: "ApiTokens",
                column: "SecretHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_UserId",
                schema: "ddt",
                table: "ApiTokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiTokens",
                schema: "ddt");

            migrationBuilder.DropColumn(
                name: "ActorTokenId",
                schema: "ddt",
                table: "AuditEvents");
        }
    }
}
