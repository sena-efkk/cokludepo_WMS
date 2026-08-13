using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wms.Modules.Inventory.Infrastructure.Persistence;

#nullable disable

namespace Wms.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(InventoryDbContext))]
    [Migration("20260813150000_AddCycleCounting")]
    public class AddCycleCounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cycle_count_task",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    risk_score_at_creation = table.Column<int>(type: "integer", nullable: false),
                    evidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    due_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    assigned_to = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    expected_quantity = table.Column<int>(type: "integer", nullable: true),
                    expected_allocated = table.Column<int>(type: "integer", nullable: true),
                    expected_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycle_count_task", x => x.id);
                    table.CheckConstraint("ck_cycle_count_task_risk_score_non_negative", "risk_score_at_creation >= 0");
                    table.CheckConstraint("ck_cycle_count_task_expected_non_negative", "expected_quantity >= 0 AND expected_allocated >= 0");
                });

            migrationBuilder.CreateTable(
                name: "cycle_count_result",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_count_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counted_quantity = table.Column<int>(type: "integer", nullable: false),
                    counted_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    counted_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    expected_quantity = table.Column<int>(type: "integer", nullable: false),
                    expected_allocated = table.Column<int>(type: "integer", nullable: false),
                    expected_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    variance = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycle_count_result", x => x.id);
                    table.CheckConstraint("ck_cycle_count_result_counted_non_negative", "counted_quantity >= 0");
                    table.CheckConstraint("ck_cycle_count_result_expected_non_negative", "expected_quantity >= 0 AND expected_allocated >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_task_sku_id_warehouse_id_location_id",
                schema: "inventory",
                table: "cycle_count_task",
                columns: new[] { "sku_id", "warehouse_id", "location_id" },
                unique: true,
                filter: "status IN ('PENDING','INPROGRESS')");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_task_warehouse_id_status_priority",
                schema: "inventory",
                table: "cycle_count_task",
                columns: new[] { "warehouse_id", "status", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_task_created_at",
                schema: "inventory",
                table: "cycle_count_task",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_result_cycle_count_task_id",
                schema: "inventory",
                table: "cycle_count_result",
                column: "cycle_count_task_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cycle_count_result",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "cycle_count_task",
                schema: "inventory");
        }
    }
}
