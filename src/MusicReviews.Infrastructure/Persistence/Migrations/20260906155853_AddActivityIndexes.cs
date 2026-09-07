using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicReviews.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Comments_UserId",
                table: "Comments");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_UserId_CreatedAt",
                table: "Reviews",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Likes_UserId_CreatedAt",
                table: "Likes",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_UserId_CreatedAt",
                table: "Comments",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reviews_UserId_CreatedAt",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Likes_UserId_CreatedAt",
                table: "Likes");

            migrationBuilder.DropIndex(
                name: "IX_Comments_UserId_CreatedAt",
                table: "Comments");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_UserId",
                table: "Comments",
                column: "UserId");
        }
    }
}
