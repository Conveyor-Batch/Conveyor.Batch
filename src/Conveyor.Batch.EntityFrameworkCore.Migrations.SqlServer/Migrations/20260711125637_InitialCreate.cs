using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conveyor.Batch.EntityFrameworkCore.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "batch_dead_letter_entries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StepName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ItemJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ItemTypeName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExceptionType = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExceptionMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StackTrace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SkipCountAtTime = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batch_dead_letter_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "batch_job_instances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batch_job_instances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "batch_job_locks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LockToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batch_job_locks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "batch_job_executions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batch_job_executions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_batch_job_executions_batch_job_instances_JobInstanceId",
                        column: x => x.JobInstanceId,
                        principalTable: "batch_job_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "batch_step_executions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StepName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    JobExecutionId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReadCount = table.Column<long>(type: "bigint", nullable: false),
                    WriteCount = table.Column<long>(type: "bigint", nullable: false),
                    SkipCount = table.Column<long>(type: "bigint", nullable: false),
                    RollbackCount = table.Column<long>(type: "bigint", nullable: false),
                    FailureMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExecutionContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batch_step_executions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_batch_step_executions_batch_job_executions_JobExecutionId",
                        column: x => x.JobExecutionId,
                        principalTable: "batch_job_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_batch_dead_letter_entries_JobName_StepName",
                table: "batch_dead_letter_entries",
                columns: new[] { "JobName", "StepName" });

            migrationBuilder.CreateIndex(
                name: "IX_batch_job_executions_JobInstanceId",
                table: "batch_job_executions",
                column: "JobInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_batch_job_instances_JobName",
                table: "batch_job_instances",
                column: "JobName");

            migrationBuilder.CreateIndex(
                name: "IX_batch_job_locks_JobName_ParametersJson",
                table: "batch_job_locks",
                columns: new[] { "JobName", "ParametersJson" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_batch_step_executions_JobExecutionId",
                table: "batch_step_executions",
                column: "JobExecutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "batch_dead_letter_entries");

            migrationBuilder.DropTable(
                name: "batch_job_locks");

            migrationBuilder.DropTable(
                name: "batch_step_executions");

            migrationBuilder.DropTable(
                name: "batch_job_executions");

            migrationBuilder.DropTable(
                name: "batch_job_instances");
        }
    }
}
