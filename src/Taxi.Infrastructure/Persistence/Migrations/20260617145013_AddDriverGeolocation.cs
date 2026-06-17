using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Taxi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverGeolocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_latitude",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "last_longitude",
                table: "drivers");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.AddColumn<Point>(
                name: "last_location",
                table: "drivers",
                type: "geography (Point)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_drivers_last_location",
                table: "drivers",
                column: "last_location")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_drivers_last_location",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "last_location",
                table: "drivers");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.AddColumn<double>(
                name: "last_latitude",
                table: "drivers",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "last_longitude",
                table: "drivers",
                type: "double precision",
                nullable: true);
        }
    }
}
