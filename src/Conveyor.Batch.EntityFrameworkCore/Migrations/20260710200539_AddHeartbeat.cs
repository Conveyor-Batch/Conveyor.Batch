using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conveyor.Batch.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddHeartbeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastHeartbeatAt",
                table: "batch_job_executions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastHeartbeatAt",
                table: "batch_job_executions");
        }
    }
}
