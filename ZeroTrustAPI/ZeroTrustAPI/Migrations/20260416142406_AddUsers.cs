using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ZeroTrustAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Username",
                table: "DeviceAccesses");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "DeviceAccesses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceAccesses_UserId",
                table: "DeviceAccesses",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceAccesses_Users_UserId",
                table: "DeviceAccesses",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeviceAccesses_Users_UserId",
                table: "DeviceAccesses");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_DeviceAccesses_UserId",
                table: "DeviceAccesses");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "DeviceAccesses");

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "DeviceAccesses",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
