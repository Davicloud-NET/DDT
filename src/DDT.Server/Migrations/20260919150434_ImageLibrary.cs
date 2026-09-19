using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDT.Server.Migrations
{
    /// <inheritdoc />
    public partial class ImageLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveDeploymentId",
                schema: "ddt",
                table: "Machines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Disks",
                schema: "ddt",
                table: "Machines",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EligibleDiskCount",
                schema: "ddt",
                table: "Machines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Images",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    WimIndex = table.Column<int>(type: "integer", nullable: false),
                    Edition = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Architecture = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    InstalledBytes = table.Column<long>(type: "bigint", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UploadedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Images_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ImageUploads",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    LastModified = table.Column<long>(type: "bigint", nullable: false),
                    Offset = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageUploads_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Deployments",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MachineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImageName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    WimIndex = table.Column<int>(type: "integer", nullable: false),
                    InstalledBytes = table.Column<long>(type: "bigint", nullable: false),
                    DiskNumber = table.Column<int>(type: "integer", nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Step = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Percent = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deployments_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "ddt",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Deployments_Images_ImageId",
                        column: x => x.ImageId,
                        principalSchema: "ddt",
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Deployments_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "ddt",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ImageId",
                schema: "ddt",
                table: "Deployments",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_MachineId",
                schema: "ddt",
                table: "Deployments",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_RequestedByUserId",
                schema: "ddt",
                table: "Deployments",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Images_Sha256",
                schema: "ddt",
                table: "Images",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_Images_UploadedByUserId",
                schema: "ddt",
                table: "Images",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageUploads_CreatedByUserId",
                schema: "ddt",
                table: "ImageUploads",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageUploads_FileName_Length_LastModified",
                schema: "ddt",
                table: "ImageUploads",
                columns: new[] { "FileName", "Length", "LastModified" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Deployments",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "ImageUploads",
                schema: "ddt");

            migrationBuilder.DropTable(
                name: "Images",
                schema: "ddt");

            migrationBuilder.DropColumn(
                name: "ActiveDeploymentId",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "Disks",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "EligibleDiskCount",
                schema: "ddt",
                table: "Machines");
        }
    }
}
