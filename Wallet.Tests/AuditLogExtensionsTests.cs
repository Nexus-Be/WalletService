namespace Wallet.Tests;

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Domain.Enums;
using Wallet.Infrastructure.Persistence;
using Xunit;

public sealed class AuditLogExtensionsTests
{
    private static WalletDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseInMemoryDatabase(databaseName: $"WalletAuditLogTestsDb_{Guid.NewGuid()}")
            .Options;
        return new WalletDbContext(options);
    }

    [Fact]
    public async Task AddAuditLog_WithObjectPayloads_SerializesToJsonAndAddsToDbContext()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var entityId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();
        var oldValues = new { Status = "Pending", Amount = 100000m };
        var newValues = new { Status = "Success", Amount = 100000m };
        var changedColumns = new[] { "Status" };

        // Act
        var result = dbContext.AddAuditLog(
            action: AuditAction.TopupRequest,
            entityName: "WalletTopup",
            entityId: entityId,
            oldValues: oldValues,
            newValues: newValues,
            userId: userId,
            userName: "wallet_user",
            changedColumns: changedColumns);

        await dbContext.SaveChangesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("wallet-service", result.ServiceName);
        Assert.Equal(AuditAction.TopupRequest, result.Action);
        Assert.Equal("WalletTopup", result.EntityName);
        Assert.Equal(entityId, result.EntityId);
        Assert.Equal(userId, result.UserId);
        Assert.Equal("wallet_user", result.UserName);
        Assert.True(result.CreatedAt <= DateTimeOffset.UtcNow);

        // Verify JSON contents
        Assert.NotNull(result.OldValues);
        using var oldDoc = JsonDocument.Parse(result.OldValues);
        Assert.Equal("Pending", oldDoc.RootElement.GetProperty("Status").GetString());

        Assert.NotNull(result.NewValues);
        using var newDoc = JsonDocument.Parse(result.NewValues);
        Assert.Equal("Success", newDoc.RootElement.GetProperty("Status").GetString());

        Assert.NotNull(result.ChangedColumns);
        using var colDoc = JsonDocument.Parse(result.ChangedColumns);
        Assert.Equal(1, colDoc.RootElement.GetArrayLength());

        // Verify persisted in DB
        var persisted = await dbContext.AuditLogs.FirstOrDefaultAsync(l => l.EntityId == entityId);
        Assert.NotNull(persisted);
        Assert.Equal("wallet-service", persisted.ServiceName);
    }
}
