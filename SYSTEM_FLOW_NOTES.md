# TÀI LIỆU TOÀN DIỆN VỀ LUỒNG NGHIỆP VỤ & CÁC BẢN VÁ HỆ THỐNG FSCM (END-TO-END SYSTEM FLOW & PATCHES)

> **Dành cho các AI Agents và Developers tiếp nối:**
> Tài liệu này mô tả chi tiết toàn bộ luồng nghiệp vụ end-to-end từ Khuyến mãi -> Đặt hàng -> Phân bổ -> Nhặt hàng -> Xuất kho -> Quét QR nhận hàng -> Công nợ -> Thu công nợ, cùng toàn bộ các bản vá lỗi đứt gãy luồng đã được triển khai và kiểm thử thành công.

---

## I. TỔNG QUAN LUỒNG NGHIỆP VỤ END-TO-END

```
[1. Khuyến mãi] ──> [2. Đặt hàng] ──> [3. Phân bổ đợt Allocation] ──> [4. Báo giá & PO]
       │                    │                           │                         │
       ▼                    ▼                           ▼                         ▼
2 loại: Campaign      Giá tạm tính              FEFO + MLOR; Tính giá       Đại lý duyệt PO;
& BatchDiscount    (FinalPricePending)        chiết khấu theo lô cận date   Khởi tạo Payment
                                                                                  │
┌─────────────────────────────────────────────────────────────────────────────────┘
│
▼
[5. Wave Picking List] ──> [6. Xuất kho F96] ──> [7. Quét QR Nhận hàng] ──> [8. Công nợ & Thu nợ]
       │                           │                          │                         │
       ▼                           ▼                          ▼                         ▼
Phương án A: Chỉ gom         Trừ tồn vật lý;            QR -> Delivered;          DueDate = DeliveredDate
đơn có PO hợp lệ;          QR -> InTransit;           Shipment -> Received;     + CreditTermDays;
Tự động nhả giữ tồn        Ghi sự kiện Dispatched     Order -> Delivered        Thu Cash/Bank/PayOS
```

---

## II. CHI TIẾT TỪNG BƯỚC & NGUYÊN TẮC HOẠT ĐỘNG

### 1. Cơ chế 2 loại khuyến mãi (Promotions)
- **Loại 1 - Khuyến mãi chiến dịch (`Campaign`):**
  - Chiết khấu % theo sản phẩm / nhóm sản phẩm / đại lý, có điều kiện số lượng tối thiểu (`MinimumQuantity`) trong thời hạn hiệu lực (`StartAt` -> `EndAt`).
  - Được tính lúc đặt hàng (`OrderService.CreateAsync`).
  - Tại thời điểm này, giá chỉ là **giá tạm tính (Indicative Price)** (`FinalPricePendingAllocation = true`) vì chưa xác định được lô hàng cụ thể nào sẽ được xuất.
- **Loại 2 - Chiết khấu theo lô / cận date (`BatchDiscount`):**
  - Chiết khấu theo thời hạn bảo quản còn lại (`RemainingShelfLifePercent`) của từng lô hàng cụ thể trong kho:
    - $\le 30\%$ HSD: gợi ý giảm 15%
    - $\le 40\%$ HSD: gợi ý giảm 10%
    - $\le 60\%$ HSD: gợi ý giảm 5%
    - Hoặc gán chủ động qua `BatchPromotion`.
- **Quy tắc xếp chồng (Stacking rule):**
  - Tại `PromotionService.CalculatePriceAsync`: Hai loại khuyến mãi **không cộng dồn**. Mức chiết khấu cao nhất sẽ thắng; nếu bằng nhau, chiết khấu theo lô cận date ưu tiên hơn chiến dịch.
  - Chiết khấu cận date chính thức được tính tại bước chạy **Allocation Run**, áp dụng cho từng lô hàng được phân bổ (`AllocationPlanPrice`).

### 2. Đặt hàng (Ordering)
- Hỗ trợ đặt hàng trực tiếp qua Sales (`CreateAsync`) hoặc gửi đơn offline đồng bộ (`SubmitOfflineAsync` - F30).
- Kiểm tra tính hợp lệ: Đại lý Active, thuộc địa bàn Sales, hạn mức tín dụng (`CreditLimit`), điều khoản công nợ (`CreditTermDays`), số lượng đặt là bội số của `orderStep`, thỏa mãn MLOR đại lý.
- Đơn tạo mới có trạng thái `PendingApproval`. Quản trị viên/Operator duyệt đơn chuyển sang `PendingAllocation`.

### 3. Đợt phân bổ (Allocation Run)
- Tự động chạy nền mỗi 3 tiếng (`AllocationRunHostedService`) hoặc kích hoạt thủ công.
- Gom toàn bộ đơn `PendingAllocation`. Thuật toán FEFO chọn lô hàng tối ưu, đảm bảo MLOR và buffer an toàn.
- Tính giá chi tiết cho từng phần lô (`AllocationPlanPrice`) bao gồm cả chiết khấu cận date.
- Phương án phân bổ có trạng thái `Proposed`.

### 4. Báo giá (`SalesOrderQuote`) & Đơn đặt hàng mua (`PurchaseOrder`)
- Quản trị viên duyệt phương án phân bổ (`AllocationPlanApprovalService.ApproveAsync`):
  - Giữ hàng tồn kho (`ReservedQuantity`).
  - Tự động phát hành `SalesOrderQuote` cho Retailer với tổng tiền `TotalAmount` đã chốt theo giá các lô được gán.
- Đại lý quyết định trên Mobile / Portal:
  - **Chấp thuận (`AcceptAsync`):** Báo giá chuyển sang `Accepted`, tự động sinh đơn đặt hàng mua `PurchaseOrder` (`PO-...`), đồng bộ `order.FinalAmount = quote.TotalAmount`, tắt cờ `FinalPricePendingAllocation`, và tự động khởi tạo sổ công nợ `Payment`.
  - **Từ chối (`RejectAsync`) hoặc Hết hạn:** Tự động giải phóng tồn kho (`OrderReleaseService.ReleaseAsync`).

### 5. Sinh phiếu nhặt hàng (Wave Picking List) - Triển khai theo Phương án A
- `RunPickingIssueService.IssueAsync`:
  - **Phương án A:** Chỉ gom các allocation của những đơn **đã có PO hợp lệ** vào phiếu nhặt hàng `RunPickingList` (theo kho) và phiếu xuất `Shipment`.
  - Đơn chưa có PO sẽ để lại đợt sau. Các đơn bị từ chối/hết hạn được auto-release giữ tồn.
  - Thủ kho quét mã nhặt từng thùng hàng (F44) và đóng phiếu nhặt hàng (F43).

### 6. Xác nhận xuất kho (Outbound Confirmation - F96)
- Thủ kho bấm xác nhận xuất kho tại kho mình phụ trách (`ShipmentService.ConfirmIssueAsync`):
  - Trừ tồn vật lý `QuantityOnHand`, trừ tồn giữ `ReservedQuantity`.
  - **Cập nhật nhãn QR (`BatchQrLabel`):** Tìm các nhãn đã nhặt (`Picked`), tự động tách lẻ (split) nếu cần, chuyển trạng thái sang `InTransit`, ghi sự kiện `QrLabelEventTypes.Dispatched` gắn với mã đơn hàng (`OrderCode`).
  - Cập nhật `Shipment.Status`: nếu đã gán tài xế -> `Delivering`, nếu chưa -> `Issued`.
  - Khi mọi shipment của đơn rời kho -> Đơn chuyển sang `Delivering`.

### 7. Quét QR nhận hàng đại lý (Receipt Confirmation - F61)
- Đại lý quét QR thùng hàng:
  - Mobile gọi `POST /api/v1/orders/{code}/confirm-receipt` với `{ qrToken, labelCodes }`.
  - Portal gọi `POST /api/v1/orders/{code}/confirm-receipt` với `{ qrToken: 'portal-demo' }`.
  - Backend `OrdersController` có cầu nối thông minh:
    - Nếu đơn có `Shipment`: Gọi `ShipmentService.ConfirmOrderShipmentsReceiptAsync`. Nhãn QR chuyển sang `Delivered`, `Shipment` chuyển sang `Received`, `Order` chuyển sang `Delivered`.
    - Trả về payload tương thích 100% cho cả Mobile (`order`, `pointsEarned`, `pointsPending`) lẫn Portal.

### 8. Ghi nhận công nợ (Debt) & Thu công nợ (Collection)
- Khi đơn hàng hoàn tất nhận hàng (`Delivered`), `PaymentPlanService.CreateForDeliveredOrderAsync` được gọi:
  - Đóng dấu ngày nhận hàng `DeliveredDate = DeliveredAt`.
  - Thiết lập kỳ hạn nợ `DueDate = DeliveredDate + CreditTermDays` (ví dụ 7, 14, 30 ngày cho đơn `Credit`).
  - Nếu quá hạn: Trạng thái tự động hiển thị là `Overdue`.
- **Thu công nợ:**
  - **Tiền mặt (`Cash`):** Chỉ Sales được thu cho đơn do chính mình tạo (`SystemRoles.Sales`).
  - **Chuyển khoản (`BankTransfer`):** Chỉ Kế toán/Operator được xác nhận (`SystemRoles.Operator`).
  - **PayOS:** Hỗ trợ tạo link thanh toán VietQR động (`POST /api/v1/payments/orders/{id}/payos-checkout`) và Webhook tự động gạch nợ `Status = Paid`.

---

## III. CÁC BẢN VÁ LỖI ĐÃ TRIỂN KHAI (PATCH LOG)

| Bản vá | File bị ảnh hưởng | Vấn đề được giải quyết |
|---|---|---|
| **Patch 1: Wave Picking Option A** | `RunPickingIssueService.cs`, `QuoteService.cs` | Trước đây nếu 1 đơn chưa có PO thì nghẽn toàn bộ đợt gom hàng; đơn từ chối/hết hạn không nhả giữ tồn. Đã sửa: Chỉ gom đơn có PO vào picking list; tự động release tồn các đơn từ chối/hết hạn. |
| **Patch 2: QR Label Dispatch & Shipment Status** | `IShipmentStore.cs`, `ShipmentStore.cs`, `ShipmentService.cs` | Trước đây xuất kho F96 không chuyển trạng thái QR sang `InTransit` và không ghi sự kiện `Dispatched`, khiến đại lý quét nhận hàng F61 luôn bị lỗi 100%. Đã sửa: Chuyển nhãn sang `InTransit`, ghi sự kiện `Dispatched`, hỗ trợ gán tài xế sau xuất kho. |
| **Patch 3: Tự động khởi tạo sổ Payment & Đồng bộ FinalAmount** | `QuoteStore.cs`, `QuoteService.cs`, `PaymentPlanService.cs` | Trước đây `Payment` record không bao giờ được tạo tự động ngoài seeder, khiến đơn `Prepaid` bị chặn xuất kho và nhận hàng văng lỗi `Conflict`. Đã sửa: Khi đại lý duyệt quote, đồng bộ `order.FinalAmount = quote.TotalAmount`, xóa cờ pending, tạo `Payment` record; thêm fallback an toàn trong `CreateForDeliveredOrderAsync`. |
| **Patch 4: Cầu nối tương thích Confirm Receipt** | `DeliveryRequests.cs`, `ShipmentService.cs`, `OrdersController.cs` | Trước đây `POST /api/v1/orders/{code}/confirm-receipt` của Mobile và Frontend bị lỗi do tìm bảng `Delivery` cũ thay vì `Shipment`, và lệch tên trường payload. Đã sửa: Nhận cả `qrCodes`, `labelCodes`, `qrToken`, tự động phân phối xác nhận qua `ShipmentService.ConfirmOrderShipmentsReceiptAsync`, trả về dữ liệu tương thích cả Mobile và Portal. |

---

## IV. BẢO CHỨNG KIỂM THỬ (TEST VERIFICATION)

Toàn bộ hệ thống đã được kiểm thử tự động và pass 100%:
- **Backend Unit Tests:** `441/441 passed` (`dotnet test tests/FSCM.UnitTests`)
- **Backend Integration Tests:** `50/50 passed` (`dotnet test tests/FSCM.IntegrationTests`)
- **Mobile Flutter Tests:** `53/53 passed` (`flutter test`)
- **Frontend Portal Build:** Build thành công 100% không lỗi (`npm run build`)

