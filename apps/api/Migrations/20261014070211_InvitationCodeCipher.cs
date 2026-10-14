using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yanyun.Api.Migrations
{
    /// <inheritdoc />
    public partial class InvitationCodeCipher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "code_cipher",
                table: "invitations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "code_cipher",
                table: "invitations");
        }
    }
}
