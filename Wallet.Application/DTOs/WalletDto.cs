namespace Wallet.Application.DTOs;

public class WalletDto
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public string Currency { get; set; } = "VND";
    public string Status { get; set; } = "ACTIVE";
    public DateTimeOffset UpdatedAt { get; set; }
}

public class WalletTransactionDto
{
    public string Id { get; set; } = string.Empty;
    public string WalletId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public decimal BalanceAfter { get; set; }
    public string Status { get; set; } = "SUCCESS";
    public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class WalletTransactionPageDto
{
    public IReadOnlyList<WalletTransactionDto> Items { get; set; } = Array.Empty<WalletTransactionDto>();
    public int Total { get; set; }
}
