using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuPlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWeekPlanRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "WeekPlans",
                type: "integer",
                nullable: false,
                // Существующие планы получают ревизию 1: 0 зарезервирован за
                // «плана ещё нет» (условие создания), и миграция не должна
                // делать вид, что план отсутствует.
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revision",
                table: "WeekPlans");
        }
    }
}
