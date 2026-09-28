using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiuTek.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutOrderPaymentFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Orders") THEN
                        RAISE EXCEPTION 'AddCheckoutOrderPaymentFoundation requires the Orders table to be empty because legacy orders cannot be assigned a trustworthy user and idempotency key automatically.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Users_UserId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StripePaymentIntentId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VNPayTransactionNo",
                table: "Orders");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutIdempotencyKey",
                table: "Orders",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Orders",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "PaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAttempts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Orders_UserId_CheckoutIdempotencyKey",
                table: "Orders",
                columns: new[] { "UserId", "CheckoutIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_OrderId_CreatedAt",
                table: "PaymentAttempts",
                columns: new[] { "OrderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_PaymentAttempts_IdempotencyKey",
                table: "PaymentAttempts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PaymentAttempts_Method_ProviderReference",
                table: "PaymentAttempts",
                columns: new[] { "Method", "ProviderReference" },
                unique: true,
                filter: "\"ProviderReference\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Users_UserId",
                table: "Orders",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Users_UserId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "PaymentAttempts");

            migrationBuilder.DropIndex(
                name: "UX_Orders_UserId_CheckoutIdempotencyKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CheckoutIdempotencyKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Orders");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "StripePaymentIntentId",
                table: "Orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VNPayTransactionNo",
                table: "Orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId",
                table: "Orders",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Users_UserId",
                table: "Orders",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
