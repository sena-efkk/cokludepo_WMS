using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wms.Modules.Inventory.Infrastructure.Persistence;

#nullable disable

namespace Wms.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(InventoryDbContext))]
    [Migration("20260813140000_AddAccuracySignals")]
    public class AddAccuracySignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_accuracy_signal",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signal_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    system_quantity_at_signal = table.Column<int>(type: "integer", nullable: false),
                    allocated_at_signal = table.Column<int>(type: "integer", nullable: false),
                    available_at_signal = table.Column<int>(type: "integer", nullable: false),
                    status_at_signal = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_accuracy_signal", x => x.id);
                    table.CheckConstraint("ck_inventory_accuracy_signal_system_quantity_non_negative", "system_quantity_at_signal >= 0");
                    table.CheckConstraint("ck_inventory_accuracy_signal_allocated_non_negative", "allocated_at_signal >= 0");
                    table.CheckConstraint("ck_inventory_accuracy_signal_available_non_negative", "available_at_signal >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_accuracy_signal_request_id",
                schema: "inventory",
                table: "inventory_accuracy_signal",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_accuracy_signal_sku_id_location_id",
                schema: "inventory",
                table: "inventory_accuracy_signal",
                columns: new[] { "sku_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_accuracy_signal_warehouse_id_signal_type",
                schema: "inventory",
                table: "inventory_accuracy_signal",
                columns: new[] { "warehouse_id", "signal_type" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_accuracy_signal_occurred_at",
                schema: "inventory",
                table: "inventory_accuracy_signal",
                column: "occurred_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_accuracy_signal",
                schema: "inventory");
        }
    }
}
