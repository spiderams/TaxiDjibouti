using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taxi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WaveDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "offered_driver_id",
                table: "rides");

            migrationBuilder.AddColumn<List<int>>(
                name: "offered_driver_ids",
                table: "rides",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'::integer[]");

            // Note : xmin est une colonne système PostgreSQL — elle n'est pas créée par la migration.
            // Le verrou optimiste est géré via la propriété ombre xmin (IsConcurrencyToken) déclarée dans RideConfiguration.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "offered_driver_ids",
                table: "rides");

            migrationBuilder.AddColumn<int>(
                name: "offered_driver_id",
                table: "rides",
                type: "integer",
                nullable: true);
        }
    }
}
