using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Modules.Transfers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_message",
                schema: "transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_message", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_message_consumer_event_id",
                schema: "transfers",
                table: "inbox_message",
                columns: new[] { "consumer", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_message_processed_at",
                schema: "transfers",
                table: "inbox_message",
                column: "processed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_message",
                schema: "transfers");
        }
    }
}
