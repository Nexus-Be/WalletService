namespace Wallet.Domain.Entities;

using Wallet.Domain.Enums;

/// <summary>
/// One balance-changing event waiting to become a journal entry. It is written in the same database transaction as the money change,
/// which is what makes it impossible to change a balance without leaving a trace to post.
/// </summary>
public class LedgerPostingPending
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WalletId { get; set; }
    public int LedgerEpoch { get; set; }
    /// <summary>Order of events inside one wallet and epoch, handed out under the wallet row lock. (WalletId, LedgerEpoch, LedgerSeq) is unique.</summary>
    public long LedgerSeq { get; set; }
    /// <summary>The first row of an epoch: it carries the balance the wallet had when the ledger started following it.</summary>
    public bool IsEpochOpening { get; set; }
    public Guid? WalletTransactionId { get; set; }
    /// <summary>Comma separated ids of every wallet transaction this event covers (a save can settle more than one).</summary>
    public string? RelatedTransactionIds { get; set; }
    public string Type { get; set; } = string.Empty;
    /// <summary>Signed change of Balance: positive when the wallet gained money.</summary>
    public decimal Delta { get; set; }
    /// <summary>Balance right after this event, captured under the wallet lock. It is the figure I10(b) compares against.</summary>
    public decimal WalletBalanceAfter { get; set; }
    public decimal OpeningBalance { get; set; }
    public LedgerPostingStatus Status { get; set; } = LedgerPostingStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PostedAt { get; set; }
}
