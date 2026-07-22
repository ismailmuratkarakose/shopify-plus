using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Order.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderBuyerRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerRef",
                schema: "ordering",
                table: "Orders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerRef",
                schema: "ordering",
                table: "Orders");
        }
    }
}
