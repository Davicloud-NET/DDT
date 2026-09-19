using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDT.Server.Migrations
{
    /// <inheritdoc />
    public partial class OpenRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnrollmentTokens",
                schema: "ddt");

            migrationBuilder.DropColumn(
                name: "EnrollmentTokenId",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstApprovedUtc",
                schema: "ddt",
                table: "Machines",
                type: "timestamp with time zone",
                nullable: true);

            // Machines approved before this column existed count as approved, including those that have fallen
            // back to Pending since, which only an approved machine's log lines still show.
            migrationBuilder.Sql(
                """
                UPDATE ddt."Machines" SET "FirstApprovedUtc" = COALESCE("ApprovedUtc", "FirstSeenUtc")
                WHERE "ApprovedUtc" IS NOT NULL
                   OR EXISTS (SELECT 1 FROM ddt."MachineLogLines" l WHERE l."MachineId" = "Machines"."Id");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Machines_FirstSeenAddress",
                schema: "ddt",
                table: "Machines",
                column: "FirstSeenAddress");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Machines_FirstSeenAddress",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "FirstApprovedUtc",
                schema: "ddt",
                table: "Machines");

            migrationBuilder.AddColumn<string>(
                name: "EnrollmentTokenId",
                schema: "ddt",
                table: "Machines",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EnrollmentTokens",
                schema: "ddt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RevokedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SecretHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentTokens", x => x.Id);
                });
        }
    }
}
