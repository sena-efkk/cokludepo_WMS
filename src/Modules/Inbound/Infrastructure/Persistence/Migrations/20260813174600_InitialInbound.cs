using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Modules.Inbound.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInbound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inbound");

            migrationBuilder.CreateTable(
                name: "inbound_putaway_task",
                schema: "inbound",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receive_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_putaway_task", x => x.id);
                    table.CheckConstraint("ck_inbound_putaway_task_quantity_positive", "quantity > 0");
                });

            migrationBuilder.CreateTable(
                name: "inbound_receipt",
                schema: "inbound",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    source_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    receiving_started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_receipt", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbound_receipt_record",
                schema: "inbound",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    receiving_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    inventory_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_receipt_record", x => x.id);
                    table.CheckConstraint("ck_inbound_receipt_record_quantity_positive", "quantity > 0");
                });

            migrationBuilder.CreateTable(
                name: "inbound_receipt_line",
                schema: "inbound",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_quantity = table.Column<int>(type: "integer", nullable: false),
                    received_quantity = table.Column<int>(type: "integer", nullable: false),
                    disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_receipt_line", x => x.id);
                    table.CheckConstraint("ck_inbound_receipt_line_expected_non_negative", "expected_quantity >= 0");
                    table.CheckConstraint("ck_inbound_receipt_line_received_non_negative", "received_quantity >= 0");
                    table.ForeignKey(
                        name: "fk_inbound_receipt_line_inbound_receipt_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "inbound",
                        principalTable: "inbound_receipt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_putaway_task_created_at",
                schema: "inbound",
                table: "inbound_putaway_task",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_putaway_task_receipt_id",
                schema: "inbound",
                table: "inbound_putaway_task",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_putaway_task_receive_record_id",
                schema: "inbound",
                table: "inbound_putaway_task",
                column: "receive_record_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_putaway_task_warehouse_id_status",
                schema: "inbound",
                table: "inbound_putaway_task",
                columns: new[] { "warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_receipt_receipt_number",
                schema: "inbound",
                table: "inbound_receipt",
                column: "receipt_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_receipt_request_id",
                schema: "inbound",
                table: "inbound_receipt",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_receipt_warehouse_id_status",
                schema: "inbound",
                table: "inbound_receipt",
                columns: new[] { "warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_receipt_line_receipt_id",
                schema: "inbound",
                table: "inbound_receipt_line",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_receipt_line_receipt_id_sku_id",
                schema: "inbound",
                table: "inbound_receipt_line",
                columns: new[] { "receipt_id", "sku_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_receipt_record_inventory_operation_id",
                schema: "inbound",
                table: "inbound_receipt_record",
                column: "inventory_operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_receipt_record_receipt_line_id",
                schema: "inbound",
                table: "inbound_receipt_record",
                column: "receipt_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_receipt_record_request_id",
                schema: "inbound",
                table: "inbound_receipt_record",
                column: "request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_putaway_task",
                schema: "inbound");

            migrationBuilder.DropTable(
                name: "inbound_receipt_line",
                schema: "inbound");

            migrationBuilder.DropTable(
                name: "inbound_receipt_record",
                schema: "inbound");

            migrationBuilder.DropTable(
                name: "inbound_receipt",
                schema: "inbound");
        }
    }
}
