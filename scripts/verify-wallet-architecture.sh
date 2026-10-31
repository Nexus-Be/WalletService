#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

failed=0
fail() { echo "FAIL: $1"; failed=1; }

# Wallet is the only service allowed to use MediatR. These checks freeze that decision.
[ -d Wallet.Application/Services ] && fail "Wallet.Application/Services exists. Use Features/{Area}/Commands|Queries/{UseCase}/."

grep -q 'Include="MediatR"' Wallet.Application/Wallet.Application.csproj \
  || fail "Wallet.Application.csproj lost the MediatR package reference."

handlers=$(find Wallet.Application/Features -name '*Handler.cs' 2>/dev/null | wc -l | tr -d ' ')
[ "$handlers" = "11" ] || fail "Expected 11 handlers under Wallet.Application/Features, found $handlers."

if grep -rInE "interface I(Wallet|AdminWallet|Topup|Checkout|Transfer|Withdrawal|VnPayCallback)Service\b" \
    --include='*.cs' Wallet.Application Wallet.Api >/dev/null 2>&1; then
  fail "Feature service facade detected. Use Command/Query handlers and inject IWalletDbContext directly."
fi

public_controllers=$(find Wallet.Api/Controllers/Public -maxdepth 1 -name '*Controller.cs' | wc -l | tr -d ' ')
internal_controllers=$(find Wallet.Api/Controllers/Internal -maxdepth 1 -name '*Controller.cs' | wc -l | tr -d ' ')
[ "$public_controllers" = "2" ] || fail "Expected 2 public controllers (Wallets, AdminWallets), found $public_controllers."
[ "$internal_controllers" = "1" ] || fail "Expected 1 internal controller, found $internal_controllers."

for controller in \
  Wallet.Api/Controllers/Public/WalletsController.cs \
  Wallet.Api/Controllers/Public/AdminWalletsController.cs \
  Wallet.Api/Controllers/Internal/InternalWalletsController.cs; do
  [ -f "$controller" ] || fail "Missing $controller."
  grep -q 'ISender' "$controller" || fail "$controller must inject ISender."
done

if [ "$failed" -eq 0 ]; then
  echo "OK: Wallet CQRS architecture is intact (11 handlers, 3 controllers, MediatR wired)."
fi
exit "$failed"
