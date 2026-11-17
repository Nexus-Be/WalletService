namespace Wallet.Tests.Support;

using Xunit;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresTestCollection
{
    public const string Name = "Disposable PostgreSQL";
}
