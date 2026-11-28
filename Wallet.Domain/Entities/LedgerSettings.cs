namespace Wallet.Domain.Entities;

/// <summary>The single row that says whether the shadow ledger is on and which epoch the current "on" period is.</summary>
public class LedgerSettings
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    /// <summary>Increases every time the ledger is switched from off to on. Wallets open a new epoch on their first event after that.</summary>
    public int ShadowEpoch { get; set; }
    /// <summary>Lets the switch command be repeated safely: a stale expected version is refused.</summary>
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; set; }
}
