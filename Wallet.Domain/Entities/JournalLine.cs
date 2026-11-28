namespace Wallet.Domain.Entities;

public class JournalLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntryId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public Guid? WalletId { get; set; }
}
