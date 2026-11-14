// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDT.Server.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class RunRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MachineHardwareHashes",
                schema: "ddt",
                columns: table => new
                {
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Hash = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false, collation: "Latin1_General_100_BIN2"),
                    GroupTag = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CollectedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadState = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    UploadMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ImportId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineHardwareHashes", x => x.MachineId);
                    table.ForeignKey(
                        name: "FK_MachineHardwareHashes_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "ddt",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryKeys",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProtectorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Volume = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProtectedPassword = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReplacedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryKeys_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "ddt",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryKeys_MachineId_ProtectorId",
                schema: "ddt",
                table: "RecoveryKeys",
                columns: new[] { "MachineId", "ProtectorId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MachineHardwareHashes",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "RecoveryKeys",
                schema: "ddt");
        }
    }
}
