namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Wallet.Infrastructure.Persistence;
using Xunit;

public sealed class MigrationConsistencyTests
{
    private static WalletDbContext NewRelationalContext() =>
        new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql("Host=localhost;Database=wallet_model_check;Username=model;Password=model")
            .Options);

    [Fact]
    public void ModelSnapshot_MatchesCurrentDesignTimeModel()
    {
        using var context = NewRelationalContext();
        var services = ((IInfrastructure<IServiceProvider>)context).Instance;
        var migrationsAssembly = services.GetRequiredService<IMigrationsAssembly>();
        var differ = services.GetRequiredService<IMigrationsModelDiffer>();
        var initializer = services.GetRequiredService<IModelRuntimeInitializer>();
        var designTimeModel = services.GetRequiredService<IDesignTimeModel>().Model;

        var snapshot = migrationsAssembly.ModelSnapshot;
        Assert.NotNull(snapshot);
        var snapshotModel = initializer.Initialize(snapshot!.Model);

        var differences = differ.GetDifferences(
            snapshotModel.GetRelationalModel(),
            designTimeModel.GetRelationalModel());

        Assert.True(
            differences.Count == 0,
            "The model snapshot is out of sync with the entity model. Pending operations: " +
            string.Join(", ", differences.Select(operation => operation.GetType().Name)));
    }

    [Fact]
    public void Migrations_HaveMetadataChronologicalOrderAndNoSeedData()
    {
        using var context = NewRelationalContext();
        var services = ((IInfrastructure<IServiceProvider>)context).Instance;
        var migrationsAssembly = services.GetRequiredService<IMigrationsAssembly>();
        var migrations = migrationsAssembly.Migrations;

        Assert.NotEmpty(migrations);
        Assert.Equal(migrations.Keys.OrderBy(key => key, StringComparer.Ordinal), migrations.Keys);

        foreach (var (id, type) in migrations)
        {
            Assert.NotNull(type.GetCustomAttribute<MigrationAttribute>());
            Assert.Equal(id, type.GetCustomAttribute<MigrationAttribute>()!.Id);
            Assert.Equal(typeof(WalletDbContext), type.GetCustomAttribute<DbContextAttribute>()?.ContextType);

            var migration = migrationsAssembly.CreateMigration(type, context.Database.ProviderName!);
            Assert.DoesNotContain(migration.UpOperations, operation => operation is InsertDataOperation);
        }
    }
}
