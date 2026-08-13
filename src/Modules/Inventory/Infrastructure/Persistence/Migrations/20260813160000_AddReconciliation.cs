using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wms.Modules.Inventory.Infrastructure.Persistence;

#nullable disable

namespace Wms.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(InventoryDbContext))]
    [Migration("20260813160000_AddReconciliation")]
    public class AddReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_reconciliation",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_count_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_count_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    expected_quantity = table.Column<int>(type: "integer", nullable: false),
                    counted_quantity = table.Column<int>(type: "integer", nullable: false),
                    variance = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    is_large_variance = table.Column<bool>(type: "boolean", nullable: false),
                    reconciliation_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_reconciliation", x => x.id);
                    table.CheckConstraint("ck_inventory_reconciliation_variance_nonzero", "variance <> 0");
                    table.CheckConstraint("ck_inventory_reconciliation_quantities_non_negative", "expected_quantity >= 0 AND counted_quantity >= 0");
                });

            migrationBuilder.CreateTable(
                name: "inventory_adjustment",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_delta = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    resolved_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_adjustment", x => x.id);
                    table.CheckConstraint("ck_inventory_adjustment_delta_nonzero", "quantity_delta <> 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reconciliation_cycle_count_result_id",
                schema: "inventory",
                table: "inventory_reconciliation",
                column: "cycle_count_result_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reconciliation_warehouse_id_reconciliation_status",
                schema: "inventory",
                table: "inventory_reconciliation",
                columns: new[] { "warehouse_id", "reconciliation_status" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reconciliation_warehouse_id_location_id_sku_id",
                schema: "inventory",
                table: "inventory_reconciliation",
                columns: new[] { "warehouse_id", "location_id", "sku_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reconciliation_created_at",
                schema: "inventory",
                table: "inventory_reconciliation",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustment_reconciliation_id",
                schema: "inventory",
                table: "inventory_adjustment",
                column: "reconciliation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustment_request_id",
                schema: "inventory",
                table: "inventory_adjustment",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustment_warehouse_id_sku_id",
                schema: "inventory",
                table: "inventory_adjustment",
                columns: new[] { "warehouse_id", "sku_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_adjustment",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "inventory_reconciliation",
                schema: "inventory");
        }
    }
}
