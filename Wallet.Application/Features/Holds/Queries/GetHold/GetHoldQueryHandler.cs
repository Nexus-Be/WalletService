namespace Wallet.Application.Features.Holds.Queries.GetHold;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Exceptions;

internal sealed class GetHoldQueryHandler(IWalletDbContext context) : IRequestHandler<GetHoldQuery, HoldStatusResponse>
{
    public async Task<HoldStatusResponse> Handle(GetHoldQuery request, CancellationToken cancellationToken)
    {
        if (request.ValidateRequest().Any())
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "The hold lookup is invalid.");
        var query = context.AuctionHolds.AsNoTracking();
        query = request.HoldId.HasValue
            ? query.Where(item => item.Id == request.HoldId.Value)
            : query.Where(item => item.ReferenceType == request.ReferenceType && item.ReferenceId == request.ReferenceId);
        var found = await query.Select(hold => new
        {
            Hold = hold,
            Purpose = context.AuctionHoldCaptures.Where(capture => capture.HoldId == hold.Id)
                .Select(capture => capture.Purpose).SingleOrDefault()
        }).SingleOrDefaultAsync(cancellationToken);
        if (found is null)
            throw new EntityNotFoundException("HOLD_NOT_FOUND", nameof(AuctionHold), request.HoldId ?? (object)request.ReferenceId!);
        var hold = found.Hold;
        return new HoldStatusResponse(hold.Id, hold.OwnerUserId, hold.ReferenceType, hold.ReferenceId,
            hold.Amount, hold.Status.ToString(), found.Purpose, hold.CreatedAt, hold.ResolvedAt);
    }
}
