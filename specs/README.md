# WALLET SERVICE SPEC KIT — CHỈ DẪN KỸ THUẬT CHO AI AGENT & DEVELOPER

Tài liệu này là **Bộ Chỉ Dẫn & Đặc Tả Kỹ Thuật (Spec Kit)** cho repository **WalletService** thuộc hệ sinh thái Microservices **Nexus**. Giúp mọi lập trình viên và AI Coding Agent (Cursor, Claude, Copilot, DeepSeek...) nắm bắt bối cảnh ngay lập tức, hiểu rõ ranh giới nghiệp vụ về ví điện tử, số dư, nạp/rút tiền, điều phối thanh toán trong Checkout Saga và **loại bỏ hoàn toàn nguy cơ ảo giác (zero hallucination)**.

---

## 1. MỤC LỤC BỘ ĐẶC TẢ WALLET SERVICE (SPECS/)

| File Đặc Tả | Trọng Tâm Nội Dung |
|:---|:---|
| [00_AGENT_MISSION_GUARDRAILS.md](00_AGENT_MISSION_GUARDRAILS.md) | Bounded Context, Port `8008`, DB `wallet_db` (`5440`), các điều cấm kỵ cốt lõi. |
| [01_CLEAN_ARCHITECTURE_LAYERS.md](01_CLEAN_ARCHITECTURE_LAYERS.md) | Cấu trúc 4 tầng Clean Architecture (`.Api`, `.Application`, `.Domain`, `.Infrastructure`), luồng phụ thuộc. |
| [02_API_GRPC_CONTRACTS.md](02_API_GRPC_CONTRACTS.md) | Hợp đồng gRPC `WalletApi` trong `shop.proto`, Request/Response messages, phân trang, tiền tệ. |
| [03_EVENT_MESSAGING.md](03_EVENT_MESSAGING.md) | Sự kiện bất đồng bộ RabbitMQ + MassTransit 8.4.1 (Trừ tiền trong Checkout Saga, bắn sự kiện hoàn tất/thất bại). |
| [04_DATA_PERSISTENCE.md](04_DATA_PERSISTENCE.md) | Mô hình thực thể `WalletAccount`, `WalletTransaction`, `BankAccount`, DbContext EF Core 8 (No Repository). |
| [05_DEV_VERIFICATION_CHECKLIST.md](05_DEV_VERIFICATION_CHECKLIST.md) | Lệnh biên dịch, kiểm thử tự động, checklist kiểm tra trước khi commit. |

---

## 2. THÔNG SỐ VẬN HÀNH CHÍNH

- **Ngôn ngữ & Runtime**: .NET 8 (C# 12).
- **Cổng gRPC Server**: Port `8008` (Mọi REST call từ client đều qua ApiGateway Port `8000`).
- **Cơ Sở Dữ Liệu**: PostgreSQL (Port `5440`, Database: `wallet_db`, Mật khẩu: `MatkhaucuaTui123@`).
- **Message Broker**: RabbitMQ (Port `5672`, Virtual Host: `/`).
- **Đơn Vị Tiền Tệ Mặc Định**: `VND`. Số dư và số tiền giao dịch lưu chuỗi decimal chuẩn 2 chữ số thập phân (ví dụ: `"500000.00"`).
