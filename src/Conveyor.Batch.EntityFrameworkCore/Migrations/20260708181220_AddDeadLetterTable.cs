using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conveyor.Batch.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddDeadLetterTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "batch_dead_letter_entries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StepName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ItemJson = table.Column<string>(type: "TEXT", nullable: false),
                    ItemTypeName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ExceptionType = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ExceptionMessage = table.Column<string>(type: "TEXT", nullable: false),
                    StackTrace = table.Column<string>(type: "TEXT", nullable: true),
                    SkipCountAtTime = table.Column<long>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batch_dead_letter_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_batch_dead_letter_entries_JobName_StepName",
                table: "batch_dead_letter_entries",
                columns: new[] { "JobName", "StepName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "batch_dead_letter_entries");
        }
    }
}
