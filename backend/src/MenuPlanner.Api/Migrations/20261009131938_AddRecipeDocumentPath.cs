using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuPlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeDocumentPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentPath",
                table: "Recipes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentPath",
                table: "Recipes");
        }
    }
}
