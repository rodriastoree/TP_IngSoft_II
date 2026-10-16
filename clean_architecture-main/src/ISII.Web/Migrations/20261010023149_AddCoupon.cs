using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ISII.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCoupon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Coupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, collation: "BINARY"),
                    DiscountPercentage = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExpirationDate = table.Column<long>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coupons", x => x.Id);
                    table.CheckConstraint("CK_Coupons_Code_MaxLength", "length(\"Code\") <= 30");
                    table.CheckConstraint("CK_Coupons_Dates", "typeof(\"CreatedAt\") = 'integer' AND typeof(\"ExpirationDate\") = 'integer' AND \"CreatedAt\" BETWEEN 0 AND 3155378975999999999 AND \"ExpirationDate\" BETWEEN 0 AND 3155378975999999999 AND \"ExpirationDate\" > \"CreatedAt\"");
                    table.CheckConstraint("CK_Coupons_IsActive", "\"IsActive\" IN (0, 1)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_Code",
                table: "Coupons",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Coupons");
        }
    }
}
