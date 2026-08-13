using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wms.Modules.Inventory.Infrastructure.Persistence;

#nullable disable

namespace Wms.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(InventoryDbContext))]
    [Migration("20260813130000_AddInventoryMovement")]
    public class AddInventoryMovement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_movement",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status_from = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status_to = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_movement", x => x.id);
                    table.CheckConstraint("ck_inventory_movement_quantity_positive", "quantity > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movement_request_id",
                schema: "inventory",
                table: "inventory_movement",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movement_warehouse_id_sku_id",
                schema: "inventory",
                table: "inventory_movement",
                columns: new[] { "warehouse_id", "sku_id" });

            migrationBuilder.AddColumn<Guid>(
                name: "movement_id",
                schema: "inventory",
                table: "inventory_ledger",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_ledger_movement_id",
                schema: "inventory",
                table: "inventory_ledger",
                column: "movement_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inventory_ledger_movement_id",
                schema: "inventory",
                table: "inventory_ledger");

            migrationBuilder.DropColumn(
                name: "movement_id",
                schema: "inventory",
                table: "inventory_ledger");

            migrationBuilder.DropTable(
                name: "inventory_movement",
                schema: "inventory");
        }
    }
}
