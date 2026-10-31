namespace Wallet.Application.Features.VnPay.Queries.ProcessVnPayReturn;

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using global::Wallet.Application.Interfaces;

internal sealed class ProcessVnPayReturnQueryHandler : IRequestHandler<ProcessVnPayReturnQuery, VnPayReturnResponse>
{
    private readonly IVnPayService _vnPay;
    private readonly ILogger<ProcessVnPayReturnQueryHandler> _logger;

    public ProcessVnPayReturnQueryHandler(IVnPayService vnPay, ILogger<ProcessVnPayReturnQueryHandler> logger)
    {
        _vnPay = vnPay;
        _logger = logger;
    }

    public Task<VnPayReturnResponse> Handle(ProcessVnPayReturnQuery request, CancellationToken cancellationToken)
    {
        if (!_vnPay.ValidateSignature(request.Parameters))
        {
            _logger.LogWarning("Invalid VNPay signature on return callback.");
            return Task.FromResult(new VnPayReturnResponse(false, null, "97", null, "Invalid signature"));
        }

        request.Parameters.TryGetValue("vnp_TxnRef", out var txnRef);
        request.Parameters.TryGetValue("vnp_ResponseCode", out var responseCode);
        request.Parameters.TryGetValue("vnp_TransactionNo", out var vnpayTransactionNo);

        var isSuccess = responseCode == "00";
        var message = isSuccess
            ? "VNPay payment completed successfully."
            : "VNPay payment failed or was cancelled.";

        return Task.FromResult(new VnPayReturnResponse(isSuccess, txnRef, responseCode, vnpayTransactionNo, message));
    }
}
