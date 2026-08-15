using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerReferenceColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reference_id",
                schema: "inventory",
                table: "inventory_ledger",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference_type",
                schema: "inventory",
                table: "inventory_ledger",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_ledger_reference_type_reference_id",
                schema: "inventory",
                table: "inventory_ledger",
                columns: new[] { "reference_type", "reference_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inventory_ledger_reference_type_reference_id",
                schema: "inventory",
                table: "inventory_ledger");

            migrationBuilder.DropColumn(
                name: "reference_id",
                schema: "inventory",
                table: "inventory_ledger");

            migrationBuilder.DropColumn(
                name: "reference_type",
                schema: "inventory",
                table: "inventory_ledger");
        }
    }
}
