using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using RiuTek.Core.Entities;
using RiuTek.Infrastructure.Data;
using RiuTek.Infrastructure.Migrations;

namespace RiuTek.Application.Test.Security;

public class OrderPaymentModelConfigurationTests
{
    [Fact]
    public void OrderModel_RequiresUserAndProtectsCheckoutIdempotencyPerUser()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Order));

        entity.Should().NotBeNull();
        entity!.FindProperty(nameof(Order.UserId))!.IsNullable.Should().BeFalse();
        entity.FindProperty(nameof(Order.CheckoutIdempotencyKey))!.GetMaxLength().Should().Be(128);
        entity.FindProperty(nameof(Order.Currency))!.GetMaxLength().Should().Be(3);

        var userForeignKey = entity.GetForeignKeys().Single(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(User));
        userForeignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);

        var idempotencyIndex = entity.GetIndexes().Single(index =>
            index.GetDatabaseName() == "UX_Orders_UserId_CheckoutIdempotencyKey");
        idempotencyIndex.IsUnique.Should().BeTrue();
        idempotencyIndex.Properties.Select(property => property.Name)
            .Should().Equal(nameof(Order.UserId), nameof(Order.CheckoutIdempotencyKey));
    }

    [Fact]
    public void PaymentAttemptModel_UsesDedicatedTableAndUniqueProviderIdentifiers()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(PaymentAttempt));

        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("PaymentAttempts");
        entity.FindProperty(nameof(PaymentAttempt.Amount))!.GetPrecision().Should().Be(18);
        entity.FindProperty(nameof(PaymentAttempt.Amount))!.GetScale().Should().Be(2);
        entity.FindProperty(nameof(PaymentAttempt.ExpiresAt))!.IsNullable.Should().BeFalse();

        entity.GetIndexes().Single(index =>
                index.GetDatabaseName() == "UX_PaymentAttempts_IdempotencyKey")
            .IsUnique.Should().BeTrue();

        var providerIndex = entity.GetIndexes().Single(index =>
            index.GetDatabaseName() == "UX_PaymentAttempts_Method_ProviderReference");
        providerIndex.IsUnique.Should().BeTrue();
        providerIndex.GetFilter().Should().Be("\"ProviderReference\" IS NOT NULL");
    }

    [Fact]
    public void Migration_BlocksDestructiveLegacyOrderConversionBeforeChangingSchema()
    {
        var migration = new TestableCheckoutOrderPaymentMigration();

        var operations = migration.GetUpOperations();

        operations.First().Should().BeOfType<SqlOperation>()
            .Which.Sql.Should().Contain("IF EXISTS (SELECT 1 FROM \"Orders\")")
            .And.Contain("requires the Orders table to be empty");
        operations.Any(operation => operation is CreateTableOperation table &&
                table.Name == "PaymentAttempts")
            .Should().BeTrue();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=riutek_model_test;Username=test;Password=test",
                npgsqlOptions => npgsqlOptions.UseVector())
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class TestableCheckoutOrderPaymentMigration : AddCheckoutOrderPaymentFoundation
    {
        public IReadOnlyList<MigrationOperation> GetUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations;
        }
    }
}
