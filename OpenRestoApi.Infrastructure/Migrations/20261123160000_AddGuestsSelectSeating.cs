using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenRestoApi.Infrastructure.Persistence;

#nullable disable

namespace OpenRestoApi.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261123160000_AddGuestsSelectSeating")]
    public partial class AddGuestsSelectSeating : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GuestsSelectSeating",
                table: "Restaurants",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuestsSelectSeating",
                table: "Restaurants");
        }
    }
}
