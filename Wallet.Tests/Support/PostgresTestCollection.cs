namespace Wallet.Tests.Support;

using System;
using Xunit;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresTestCollection
{
    public const string Name = "Disposable PostgreSQL";
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DisposablePostgresFactAttribute : FactAttribute
{
    public DisposablePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")))
        {
            Skip = "Set WALLET_STRIPE_TEST_DATABASE to a disposable PostgreSQL database to run this integration test.";
        }
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DisposablePostgresTheoryAttribute : TheoryAttribute
{
    public DisposablePostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")))
        {
            Skip = "Set WALLET_STRIPE_TEST_DATABASE to a disposable PostgreSQL database to run this integration test.";
        }
    }
}
