# 05 — DANH SÁCH KIỂM CHỨNG & NGHIỆM THU (WALLET SERVICE CHECKLIST)

---

## 1. LỆNH BIÊN DỊCH & CHẠY THỬ NGHIỆM

```bash
# 1. Biên dịch solution Wallet
dotnet build Wallet.sln -c Release

# 2. Khởi chạy service (gRPC port 8008)
dotnet run --project Wallet.Api

# 3. Thử nghiệm qua ApiGateway:
# Mở gateway.runtime.json, kiểm tra "wallet": { "Mode": "Real", "Address": "http://localhost:8008" }
# Gọi GET /api/v1/wallet/balance trên Swagger Gateway (port 8000) xem kết quả thật trả về.
```

---

## 2. CHECKLIST TRƯỚC KHI COMMIT CHO AI AGENTS

- [ ] **Biên dịch thành công 100%**: Không lỗi biên dịch, không cảnh báo nghiêm trọng.
- [ ] **Không vi phạm rào chắn**: Không dùng Generic Repository Pattern bọc ngoài EF Core.
- [ ] **Port & Database**: Lắng nghe đúng port `8008`, kết nối `wallet_db` trên port `5439`.
- [ ] **Bảo vệ số dư**: Thao tác trừ tiền, rút tiền, chuyển tiền luôn kiểm tra số dư khả dụng, không để âm ví.
- [ ] **Git Status**: Không commit file build rác (`bin/`, `obj/`, `.idea/`, `.env`).
