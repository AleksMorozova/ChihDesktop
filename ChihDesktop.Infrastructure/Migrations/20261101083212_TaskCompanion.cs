using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChihDesktop.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaskCompanion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DeliveredAtUtcTicks",
                table: "PlannerTasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RemindAtUtcTicks",
                table: "PlannerTasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderStatus",
                table: "PlannerTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SnoozedUntilUtcTicks",
                table: "PlannerTasks",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveredAtUtcTicks",
                table: "PlannerTasks");

            migrationBuilder.DropColumn(
                name: "RemindAtUtcTicks",
                table: "PlannerTasks");

            migrationBuilder.DropColumn(
                name: "ReminderStatus",
                table: "PlannerTasks");

            migrationBuilder.DropColumn(
                name: "SnoozedUntilUtcTicks",
                table: "PlannerTasks");
        }
    }
}
