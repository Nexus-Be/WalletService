namespace Wallet.Domain.Exceptions;

public class InsufficientBalanceException : Exception
{
    public InsufficientBalanceException(string message) : base(message)
    {
    }

    public InsufficientBalanceException(decimal currentBalance, decimal requestedAmount)
        : base($"Số dư ví không đủ. Hiện tại: {currentBalance}, yêu cầu: {requestedAmount}")
    {
    }
}
