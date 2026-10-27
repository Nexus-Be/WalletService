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
        : base($"Insufficient wallet balance. Current: {currentBalance}, requested: {requestedAmount}") { }
}

public class WalletNotFoundException : DomainException
{
    public WalletNotFoundException(string userId)
        : base($"Wallet for user '{userId}' was not found.") { }
}
