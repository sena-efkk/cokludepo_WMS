using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wms.Modules.MasterData.Infrastructure.Persistence;

#nullable disable

namespace Wms.Modules.MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MasterDataDbContext))]
    [Migration("20260813130000_AddSkuCodeSequence")]
    public class AddSkuCodeSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "sku_code_seq",
                schema: "master_data",
                startValue: 1L,
                incrementBy: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "sku_code_seq",
                schema: "master_data");
        }
    }
}
