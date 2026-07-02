using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conveyor.Batch.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "batch_job_instances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ParametersJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batch_job_instances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "batch_job_executions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobInstanceId = table.Column<long>(type: "INTEGER", nullable: false),
                    ParametersJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FailureMessage = table.Column<string>(type: "TEXT", nullable: true)
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
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StepName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    JobExecutionId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReadCount = table.Column<long>(type: "INTEGER", nullable: false),
                    WriteCount = table.Column<long>(type: "INTEGER", nullable: false),
                    SkipCount = table.Column<long>(type: "INTEGER", nullable: false),
                    RollbackCount = table.Column<long>(type: "INTEGER", nullable: false),
                    FailureMessage = table.Column<string>(type: "TEXT", nullable: true)
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
                name: "IX_batch_job_executions_JobInstanceId",
                table: "batch_job_executions",
                column: "JobInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_batch_job_instances_JobName",
                table: "batch_job_instances",
                column: "JobName");

            migrationBuilder.CreateIndex(
                name: "IX_batch_step_executions_JobExecutionId",
                table: "batch_step_executions",
                column: "JobExecutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "batch_step_executions");

            migrationBuilder.DropTable(
                name: "batch_job_executions");

            migrationBuilder.DropTable(
                name: "batch_job_instances");
        }
    }
}
