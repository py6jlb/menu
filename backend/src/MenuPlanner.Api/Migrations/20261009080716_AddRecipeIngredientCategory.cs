using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuPlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeIngredientCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "RecipeIngredients",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "RecipeIngredients");
        }
    }
}
