namespace Wallet.Domain.Entities;

public class JournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WalletId { get; set; }
    public Guid? PendingId { get; set; }
    public Guid? WalletTransactionId { get; set; }
    /// <summary>Live (posted from a pending row) or Backfill (rebuilt from history).</summary>
    public string Source { get; set; } = "Live";
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<JournalLine> Lines { get; set; } = new List<JournalLine>();
}
