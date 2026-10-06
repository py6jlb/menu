using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuPlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Recipes",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Recipes");
        }
    }
}
