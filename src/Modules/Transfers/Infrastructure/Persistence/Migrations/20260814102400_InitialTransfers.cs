using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Modules.Transfers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "transfers");

            migrationBuilder.CreateTable(
                name: "transfer_discrepancy",
                schema: "transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_discrepancy", x => x.id);
                    table.CheckConstraint("ck_transfer_discrepancy_quantity_positive", "quantity > 0");
                });

            migrationBuilder.CreateTable(
                name: "transfer_order",
                schema: "transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    outbound_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inbound_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    shipped_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_order", x => x.id);
                    table.CheckConstraint("ck_transfer_order_distinct_warehouses", "source_warehouse_id <> destination_warehouse_id");
                });

            migrationBuilder.CreateTable(
                name: "transfer_receive_record",
                schema: "transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    receiving_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_receive_record", x => x.id);
                    table.CheckConstraint("ck_transfer_receive_record_quantity_positive", "quantity > 0");
                });

            migrationBuilder.CreateTable(
                name: "transfer_line",
                schema: "transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_quantity = table.Column<int>(type: "integer", nullable: false),
                    shipped_quantity = table.Column<int>(type: "integer", nullable: false),
                    received_quantity = table.Column<int>(type: "integer", nullable: false),
                    confirmed_variance_quantity = table.Column<int>(type: "integer", nullable: false),
                    outbound_order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inbound_receipt_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_line", x => x.id);
                    table.CheckConstraint("ck_transfer_line_no_negative_intransit", "received_quantity + confirmed_variance_quantity <= shipped_quantity");
                    table.CheckConstraint("ck_transfer_line_received_non_negative", "received_quantity >= 0");
                    table.CheckConstraint("ck_transfer_line_requested_positive", "requested_quantity > 0");
                    table.CheckConstraint("ck_transfer_line_shipped_non_negative", "shipped_quantity >= 0");
                    table.CheckConstraint("ck_transfer_line_variance_non_negative", "confirmed_variance_quantity >= 0");
                    table.ForeignKey(
                        name: "fk_transfer_line_transfer_orders_transfer_order_id",
                        column: x => x.transfer_order_id,
                        principalSchema: "transfers",
                        principalTable: "transfer_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_discrepancy_request_id",
                schema: "transfers",
                table: "transfer_discrepancy",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfer_discrepancy_transfer_line_id",
                schema: "transfers",
                table: "transfer_discrepancy",
                column: "transfer_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_line_transfer_order_id",
                schema: "transfers",
                table: "transfer_line",
                column: "transfer_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_line_transfer_order_id_sku_id",
                schema: "transfers",
                table: "transfer_line",
                columns: new[] { "transfer_order_id", "sku_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfer_order_destination_warehouse_id_status",
                schema: "transfers",
                table: "transfer_order",
                columns: new[] { "destination_warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_order_request_id",
                schema: "transfers",
                table: "transfer_order",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfer_order_source_warehouse_id_status",
                schema: "transfers",
                table: "transfer_order",
                columns: new[] { "source_warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_order_transfer_number",
                schema: "transfers",
                table: "transfer_order",
                column: "transfer_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfer_receive_record_request_id",
                schema: "transfers",
                table: "transfer_receive_record",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfer_receive_record_transfer_line_id",
                schema: "transfers",
                table: "transfer_receive_record",
                column: "transfer_line_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transfer_discrepancy",
                schema: "transfers");

            migrationBuilder.DropTable(
                name: "transfer_line",
                schema: "transfers");

            migrationBuilder.DropTable(
                name: "transfer_receive_record",
                schema: "transfers");

            migrationBuilder.DropTable(
                name: "transfer_order",
                schema: "transfers");
        }
    }
}
