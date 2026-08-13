using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Modules.Facility.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFacility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "facility");

            migrationBuilder.CreateTable(
                name: "warehouse",
                schema: "facility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    address_line = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouse", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "location",
                schema: "facility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    allows_picking = table.Column<bool>(type: "boolean", nullable: false),
                    allows_putaway = table.Column<bool>(type: "boolean", nullable: false),
                    allows_replenishment = table.Column<bool>(type: "boolean", nullable: false),
                    holds_inventory = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location", x => x.id);
                    table.ForeignKey(
                        name: "fk_location_location_parent_location_id",
                        column: x => x.parent_location_id,
                        principalSchema: "facility",
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_location_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalSchema: "facility",
                        principalTable: "warehouse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_location_parent_location_id",
                schema: "facility",
                table: "location",
                column: "parent_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_warehouse_id",
                schema: "facility",
                table: "location",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_warehouse_id_code",
                schema: "facility",
                table: "location",
                columns: new[] { "warehouse_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_code",
                schema: "facility",
                table: "warehouse",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_name",
                schema: "facility",
                table: "warehouse",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "location",
                schema: "facility");

            migrationBuilder.DropTable(
                name: "warehouse",
                schema: "facility");
        }
    }
}
