using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class MonitoringPinRetirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonitoringPinReservations",
                columns: table => new
                {
                    PinLookup = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedPin = table.Column<string>(type: "TEXT", nullable: true),
                    ProtectionId = table.Column<string>(type: "TEXT", nullable: true),
                    DeletionPending = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringPinReservations", x => x.PinLookup);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonitoringPinReservations");
        }
    }
}
