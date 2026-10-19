namespace Wallet.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}

public class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string entityName, object key)
        : base($"Entity '{entityName}' with key '{key}' was not found.") { }
}

public class BusinessRuleValidationException : DomainException
{
    public BusinessRuleValidationException(string message) : base(message) { }
}

public class InsufficientBalanceException : DomainException
{
    public InsufficientBalanceException(string message) : base(message) { }

    public InsufficientBalanceException(decimal currentBalance, decimal requestedAmount)
        : base($"Số dư ví không đủ. Hiện tại: {currentBalance}, yêu cầu: {requestedAmount}") { }
}

public class WalletNotFoundException : DomainException
{
    public WalletNotFoundException(string userId)
        : base($"Không tìm thấy ví của người dùng '{userId}'.") { }
}
