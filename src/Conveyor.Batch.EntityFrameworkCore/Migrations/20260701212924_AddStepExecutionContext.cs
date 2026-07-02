using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conveyor.Batch.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddStepExecutionContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutionContextJson",
                table: "batch_step_executions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutionContextJson",
                table: "batch_step_executions");
        }
    }
}
