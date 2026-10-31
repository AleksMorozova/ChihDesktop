using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChihDesktop.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlanner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlannerTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannerTasks", x => x.Id);
                    table.CheckConstraint("CK_PlannerTasks_Category", "\"Category\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_PlannerTasks_Completion", "(\"Status\" = 0 AND \"CompletedAtUtcTicks\" IS NULL) OR (\"Status\" = 1 AND \"CompletedAtUtcTicks\" IS NOT NULL)");
                    table.CheckConstraint("CK_PlannerTasks_Priority", "\"Priority\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_PlannerTasks_Revision", "\"Revision\" >= 1");
                    table.CheckConstraint("CK_PlannerTasks_Status", "\"Status\" IN (0, 1)");
                    table.CheckConstraint("CK_PlannerTasks_Title", "length(trim(\"Title\")) BETWEEN 1 AND 200");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlannerTasks_Status_CreatedAtUtcTicks",
                table: "PlannerTasks",
                columns: new[] { "Status", "CreatedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_PlannerTasks_Status_DueDate_Priority_CreatedAtUtcTicks",
                table: "PlannerTasks",
                columns: new[] { "Status", "DueDate", "Priority", "CreatedAtUtcTicks" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlannerTasks");
        }
    }
}
