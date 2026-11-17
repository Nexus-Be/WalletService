namespace Wallet.Application.Features.Topups.Commands.ResumeTopup;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record ResumeTopupCommand(
    [NonEmptyGuid] Guid TransactionId) : IRequest<ResumeTopupResponse>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var error = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (error is not null) yield return error;
    }
}

public sealed record ResumeTopupResponse(Guid TransactionId, string PaymentUrl, DateTimeOffset SessionExpiresAt);
