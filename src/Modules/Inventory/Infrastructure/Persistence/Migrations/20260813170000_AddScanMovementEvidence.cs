using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wms.Modules.Inventory.Infrastructure.Persistence;

#nullable disable

namespace Wms.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(InventoryDbContext))]
    [Migration("20260813170000_AddScanMovementEvidence")]
    public class AddScanMovementEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scan_movement_evidence",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_scan_value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sku_scan_value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    destination_scan_value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    operator_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    occurred_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scan_movement_evidence", x => x.id);
                    table.CheckConstraint("ck_scan_movement_evidence_quantity_positive", "quantity > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_scan_movement_evidence_movement_id",
                schema: "inventory",
                table: "scan_movement_evidence",
                column: "movement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scan_movement_evidence_request_id",
                schema: "inventory",
                table: "scan_movement_evidence",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_movement_evidence_warehouse_id_sku_id",
                schema: "inventory",
                table: "scan_movement_evidence",
                columns: new[] { "warehouse_id", "sku_id" });

            migrationBuilder.CreateIndex(
                name: "ix_scan_movement_evidence_occurred_at",
                schema: "inventory",
                table: "scan_movement_evidence",
                column: "occurred_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scan_movement_evidence",
                schema: "inventory");
        }
    }
}
