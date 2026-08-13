using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "inventory_balance",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    allocated = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_balance", x => x.id);
                    table.CheckConstraint("ck_inventory_balance_allocated_non_negative", "allocated >= 0");
                    table.CheckConstraint("ck_inventory_balance_allocated_not_exceeds_quantity", "allocated <= quantity");
                    table.CheckConstraint("ck_inventory_balance_allocated_only_available", "status = 'AVAILABLE' OR allocated = 0");
                    table.CheckConstraint("ck_inventory_balance_quantity_non_negative", "quantity >= 0");
                });

            migrationBuilder.CreateTable(
                name: "inventory_ledger",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    entry_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    quantity_delta = table.Column<int>(type: "integer", nullable: false),
                    allocated_delta = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_ledger", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_operation",
                schema: "inventory",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_operation", x => x.request_id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_reservation",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_quantity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_reservation", x => x.id);
                    table.CheckConstraint("ck_inventory_reservation_requested_positive", "requested_quantity > 0");
                });

            migrationBuilder.CreateTable(
                name: "inventory_reservation_line",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_reservation_line", x => x.id);
                    table.CheckConstraint("ck_inventory_reservation_line_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_inventory_reservation_line_inventory_reservation_reservatio",
                        column: x => x.reservation_id,
                        principalSchema: "inventory",
                        principalTable: "inventory_reservation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balance_sku_id_warehouse_id_location_id_status",
                schema: "inventory",
                table: "inventory_balance",
                columns: new[] { "sku_id", "warehouse_id", "location_id", "status" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balance_warehouse_id_location_id",
                schema: "inventory",
                table: "inventory_balance",
                columns: new[] { "warehouse_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balance_warehouse_id_sku_id_status",
                schema: "inventory",
                table: "inventory_balance",
                columns: new[] { "warehouse_id", "sku_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_ledger_request_id",
                schema: "inventory",
                table: "inventory_ledger",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_ledger_warehouse_id",
                schema: "inventory",
                table: "inventory_ledger",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_ledger_warehouse_id_sku_id",
                schema: "inventory",
                table: "inventory_ledger",
                columns: new[] { "warehouse_id", "sku_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservation_request_id",
                schema: "inventory",
                table: "inventory_reservation",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservation_warehouse_id_sku_id",
                schema: "inventory",
                table: "inventory_reservation",
                columns: new[] { "warehouse_id", "sku_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservation_line_location_id",
                schema: "inventory",
                table: "inventory_reservation_line",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservation_line_reservation_id",
                schema: "inventory",
                table: "inventory_reservation_line",
                column: "reservation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_balance",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "inventory_ledger",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "inventory_operation",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "inventory_reservation_line",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "inventory_reservation",
                schema: "inventory");
        }
    }
}
