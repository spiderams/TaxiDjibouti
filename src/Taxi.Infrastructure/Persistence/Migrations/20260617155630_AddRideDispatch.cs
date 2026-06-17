using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taxi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRideDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "offer_expires_at",
                table: "rides",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "offered_driver_id",
                table: "rides",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<List<int>>(
                name: "tried_driver_ids",
                table: "rides",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "offer_expires_at",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "offered_driver_id",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "tried_driver_ids",
                table: "rides");
        }
    }
}
