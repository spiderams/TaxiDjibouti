using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Taxi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rides",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    driver_id = table.Column<int>(type: "integer", nullable: true),
                    pickup_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    destination_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    pickup_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    destination_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pickup_latitude = table.Column<double>(type: "double precision", nullable: true),
                    pickup_longitude = table.Column<double>(type: "double precision", nullable: true),
                    destination_latitude = table.Column<double>(type: "double precision", nullable: true),
                    destination_longitude = table.Column<double>(type: "double precision", nullable: true),
                    estimated_price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rides", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rides_client_id",
                table: "rides",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_rides_driver_id",
                table: "rides",
                column: "driver_id");

            migrationBuilder.CreateIndex(
                name: "ix_rides_status",
                table: "rides",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rides");
        }
    }
}
