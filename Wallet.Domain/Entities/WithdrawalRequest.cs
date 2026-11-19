using Wallet.Domain.Base;
using Wallet.Domain.Enums;

namespace Wallet.Domain.Entities;

public class WithdrawalRequest : BaseEntity<Guid>
{
    public required Guid WalletId { get; set; }
    public required decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public required decimal NetAmount { get; set; }
    public required string BankName { get; set; }
    public required string BankAccountNumber { get; set; }
    public required string BankAccountHolder { get; set; }
    public required WithdrawalStatus Status { get; set; } = WithdrawalStatus.Pending;
    public Guid? ProcessedBy { get; set; }
    public string? GatewayReferenceId { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public Guid? BankAccountId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? StatusReason { get; set; }
    public string? ClientIdempotencyKey { get; set; }
    public string? RequestFingerprint { get; set; }

    // Navigation
    public WalletAccount? Wallet { get; set; }
}
