using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Modules.Outbound.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOutbound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "outbound");

            migrationBuilder.CreateTable(
                name: "outbound_fulfillment_order",
                schema: "outbound",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_order_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    allocated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    picking_started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    packed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    shipped_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_fulfillment_order", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbound_package",
                schema: "outbound",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    packed_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_package", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbound_pick_task",
                schema: "outbound",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    required_quantity = table.Column<int>(type: "integer", nullable: false),
                    picked_quantity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_pick_task", x => x.id);
                    table.CheckConstraint("ck_outbound_pick_task_picked_non_negative", "picked_quantity >= 0");
                    table.CheckConstraint("ck_outbound_pick_task_picked_not_exceeds_required", "picked_quantity <= required_quantity");
                    table.CheckConstraint("ck_outbound_pick_task_required_positive", "required_quantity > 0");
                });

            migrationBuilder.CreateTable(
                name: "outbound_shipment",
                schema: "outbound",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    carrier_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    shipped_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_shipment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbound_fulfillment_order_line",
                schema: "outbound",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_quantity = table.Column<int>(type: "integer", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_fulfillment_order_line", x => x.id);
                    table.CheckConstraint("ck_outbound_fulfillment_order_line_quantity_positive", "requested_quantity > 0");
                    table.ForeignKey(
                        name: "fk_outbound_fulfillment_order_line_outbound_fulfillment_order_",
                        column: x => x.order_id,
                        principalSchema: "outbound",
                        principalTable: "outbound_fulfillment_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_fulfillment_order_order_number",
                schema: "outbound",
                table: "outbound_fulfillment_order",
                column: "order_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_fulfillment_order_request_id",
                schema: "outbound",
                table: "outbound_fulfillment_order",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_fulfillment_order_warehouse_id_status",
                schema: "outbound",
                table: "outbound_fulfillment_order",
                columns: new[] { "warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_fulfillment_order_line_order_id",
                schema: "outbound",
                table: "outbound_fulfillment_order_line",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_fulfillment_order_line_order_id_sku_id",
                schema: "outbound",
                table: "outbound_fulfillment_order_line",
                columns: new[] { "order_id", "sku_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_package_order_id",
                schema: "outbound",
                table: "outbound_package",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_package_package_number",
                schema: "outbound",
                table: "outbound_package",
                column: "package_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_package_request_id",
                schema: "outbound",
                table: "outbound_package",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_pick_task_created_at",
                schema: "outbound",
                table: "outbound_pick_task",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_pick_task_order_id",
                schema: "outbound",
                table: "outbound_pick_task",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_pick_task_reservation_line_id",
                schema: "outbound",
                table: "outbound_pick_task",
                column: "reservation_line_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_pick_task_warehouse_id_status",
                schema: "outbound",
                table: "outbound_pick_task",
                columns: new[] { "warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_shipment_order_id",
                schema: "outbound",
                table: "outbound_shipment",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_shipment_request_id",
                schema: "outbound",
                table: "outbound_shipment",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_shipment_shipment_number",
                schema: "outbound",
                table: "outbound_shipment",
                column: "shipment_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbound_fulfillment_order_line",
                schema: "outbound");

            migrationBuilder.DropTable(
                name: "outbound_package",
                schema: "outbound");

            migrationBuilder.DropTable(
                name: "outbound_pick_task",
                schema: "outbound");

            migrationBuilder.DropTable(
                name: "outbound_shipment",
                schema: "outbound");

            migrationBuilder.DropTable(
                name: "outbound_fulfillment_order",
                schema: "outbound");
        }
    }
}
