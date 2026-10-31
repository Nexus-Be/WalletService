# 05 — ERROR HANDLING & ENUM BOUNDARY (WALLET SERVICE)

## 1. 100% English & Sentence Case
* Error messages must be 100% English sentence case.
* Capitalize only the first letter. Keep technical acronyms uppercase (`ID`, `VNPAY`, `OTP`, `UUID`).
* Examples: `"Insufficient balance to complete the transaction."`, `"Wallet account not found for the specified user."`, `"Transfer amount must be greater than zero."`

## 2. SCREAMING_SNAKE_CASE Error Codes
* Error codes must be uppercase with underscores. No `ERR_` prefixes.
* Use `VAL_` prefix for validation errors.
* Examples: `WALLET_NOT_FOUND`, `INSUFFICIENT_BALANCE`, `VAL_INVALID_AMOUNT`, `VAL_INVALID_USER_ID`, `UNAUTHORIZED`, `FORBIDDEN`.

## 3. RFC 9110 ProblemDetails Structure
* Handled by `GlobalExceptionHandlerMiddleware.cs`: returns `application/problem+json` containing `type`, `title`, `status`, `errorCode`, `detail`, `traceId`.

## 4. Enum Persistence & Boundary Serialization
* In PostgreSQL (`wallet_db`): 100% stored as `integer` (int32). Prohibit `HasConversion<string>()`.
* Enums: `TransactionType` (`Topup = 1`, `Withdrawal = 2`, `Transfer = 3`, `Checkout = 4`), `TransactionStatus` (`Pending = 0`, `Success = 1`, `Failed = 2`).
* REST Outbound: 100% serialized as `PascalCase` strings (`"Topup"`, `"Success"`).
* REST Inbound: Handled via `PascalCaseJsonStringEnumConverter(allowIntegerValues: false)`.
