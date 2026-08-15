using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Modules.Fulfillment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fulfillment");

            migrationBuilder.CreateTable(
                name: "fulfillment_sourcing_decision",
                schema: "fulfillment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sourcing_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    committed_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fulfillment_sourcing_decision", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fulfillment_sourcing_order_link",
                schema: "fulfillment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outbound_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fulfillment_sourcing_order_link", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fulfillment_sourcing_request",
                schema: "fulfillment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fulfillment_sourcing_request", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fulfillment_sourcing_line",
                schema: "fulfillment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sourcing_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fulfillment_sourcing_line", x => x.id);
                    table.CheckConstraint("ck_fulfillment_sourcing_line_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_fulfillment_sourcing_line_sourcing_requests_sourcing_reques",
                        column: x => x.sourcing_request_id,
                        principalSchema: "fulfillment",
                        principalTable: "fulfillment_sourcing_request",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_sourcing_decision_request_id",
                schema: "fulfillment",
                table: "fulfillment_sourcing_decision",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_sourcing_decision_sourcing_request_id",
                schema: "fulfillment",
                table: "fulfillment_sourcing_decision",
                column: "sourcing_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_sourcing_line_sourcing_request_id",
                schema: "fulfillment",
                table: "fulfillment_sourcing_line",
                column: "sourcing_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_sourcing_line_sourcing_request_id_sku_id",
                schema: "fulfillment",
                table: "fulfillment_sourcing_line",
                columns: new[] { "sourcing_request_id", "sku_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_sourcing_order_link_decision_id",
                schema: "fulfillment",
                table: "fulfillment_sourcing_order_link",
                column: "decision_id");

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_sourcing_order_link_outbound_order_id",
                schema: "fulfillment",
                table: "fulfillment_sourcing_order_link",
                column: "outbound_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_sourcing_request_request_id",
                schema: "fulfillment",
                table: "fulfillment_sourcing_request",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_sourcing_request_status",
                schema: "fulfillment",
                table: "fulfillment_sourcing_request",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fulfillment_sourcing_decision",
                schema: "fulfillment");

            migrationBuilder.DropTable(
                name: "fulfillment_sourcing_line",
                schema: "fulfillment");

            migrationBuilder.DropTable(
                name: "fulfillment_sourcing_order_link",
                schema: "fulfillment");

            migrationBuilder.DropTable(
                name: "fulfillment_sourcing_request",
                schema: "fulfillment");
        }
    }
}
