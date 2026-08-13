using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Wms.Modules.MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "master_data");

            migrationBuilder.CreateTable(
                name: "brand",
                schema: "master_data",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brand", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "category",
                schema: "master_data",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product",
                schema: "master_data",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "uom",
                schema: "master_data",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uom", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sku",
                schema: "master_data",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    uom_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    length_cm = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    width_cm = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sku", x => x.id);
                    table.CheckConstraint("ck_sku_measurements_non_negative", "weight_kg >= 0 AND length_cm >= 0 AND width_cm >= 0 AND height_cm >= 0");
                    table.ForeignKey(
                        name: "fk_sku_product_product_id",
                        column: x => x.product_id,
                        principalSchema: "master_data",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sku_uoms_uom_id",
                        column: x => x.uom_id,
                        principalSchema: "master_data",
                        principalTable: "uom",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sku_barcode",
                schema: "master_data",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sku_barcode", x => x.id);
                    table.ForeignKey(
                        name: "fk_sku_barcode_skus_sku_id",
                        column: x => x.sku_id,
                        principalSchema: "master_data",
                        principalTable: "sku",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "master_data",
                table: "uom",
                columns: new[] { "id", "code", "name" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "EA", "Each" },
                    { new Guid("10000000-0000-0000-0000-000000000002"), "BOX", "Box" },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "PCS", "Piece" },
                    { new Guid("10000000-0000-0000-0000-000000000004"), "KG", "Kilogram" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_brand_name",
                schema: "master_data",
                table: "brand",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_category_name",
                schema: "master_data",
                table: "category",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_name",
                schema: "master_data",
                table: "product",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_sku_code",
                schema: "master_data",
                table: "sku",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sku_product_id",
                schema: "master_data",
                table: "sku",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_sku_uom_id",
                schema: "master_data",
                table: "sku",
                column: "uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_sku_barcode_sku_id",
                schema: "master_data",
                table: "sku_barcode",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "ix_sku_barcode_value",
                schema: "master_data",
                table: "sku_barcode",
                column: "value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_uom_code",
                schema: "master_data",
                table: "uom",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "brand",
                schema: "master_data");

            migrationBuilder.DropTable(
                name: "category",
                schema: "master_data");

            migrationBuilder.DropTable(
                name: "sku_barcode",
                schema: "master_data");

            migrationBuilder.DropTable(
                name: "sku",
                schema: "master_data");

            migrationBuilder.DropTable(
                name: "product",
                schema: "master_data");

            migrationBuilder.DropTable(
                name: "uom",
                schema: "master_data");
        }
    }
}
