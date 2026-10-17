namespace Wallet.Domain.Exceptions;

public class WalletNotFoundException : Exception
{
    public WalletNotFoundException(string userId)
        : base($"Không tìm thấy ví của người dùng '{userId}'.")
    {
    }
}
