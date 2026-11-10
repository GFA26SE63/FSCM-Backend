Gate wave picking by PO, dispatch QR labels and automate receipt debt ledger

Khép kín luồng nghiệp vụ end-to-end từ duyệt báo giá, PO, gom nhặt hàng wave picking (Phương án A), xác nhận xuất kho F96, quét QR nhận hàng F61 đến tự động hoá sổ công nợ và thu nợ:

## 1. Phương án A: Lọc đơn có PO vào Wave Picking & Tự động nhả giữ tồn
- `RunPickingIssueService.IssueAsync`: Chỉ gom allocations của những đơn hàng đã chấp thuận báo giá và có Purchase Order (`PO-...`) hợp lệ vào `RunPickingList` và `Shipment`. Tránh tình trạng 1 đơn chưa duyệt PO làm tắc nghẽn toàn bộ đợt gom hàng.
- Tự động gọi `OrderReleaseService.ReleaseAsync` để nhả lượng tồn giữ (`ReservedQuantity`) cho các đơn có báo giá bị từ chối (`Rejected`) hoặc hết hạn (`Expired`).
- Tự động nhả tồn ngay tại `QuoteService.RejectAsync` và khi kiểm tra hết hạn trong `AcceptAsync`.

## 2. Xuất kho F96: Chuyển nhãn QR sang InTransit & Ghi sự kiện Dispatched
- `ShipmentService.ConfirmIssueAsync`: Lấy toàn bộ nhãn QR đã nhặt (`Picked`) của từng shipment batch, tự động tách lẻ label (split) nếu số lượng lẻ, chuyển trạng thái sang `InTransit`, và ghi nhận sự kiện `QrLabelEventTypes.Dispatched` gắn với mã đơn hàng (`OrderCode`).
- Khắc phục lỗi 100% không thể nhận hàng ở bước đại lý quét QR (F61).
- Quản lý trạng thái `Shipment` linh hoạt: nếu đã có tài xế chuyển sang `Delivering`, nếu chưa có chuyển sang `Issued`. Cho phép gán tài xế sau khi đã xuất kho (`AssignAsync`).

## 3. Tự động khởi tạo sổ Payment & Đồng bộ FinalAmount theo lô cận date
- `QuoteService.AcceptAsync`: Khi đại lý chấp thuận báo giá, đồng bộ lại `order.FinalAmount = quote.TotalAmount` (giá đã chốt chiết khấu lô cận date), tắt cờ `FinalPricePendingAllocation`, và tự động gọi `paymentPlanService.CreateForFinalApprovedOrderAsync` để khởi tạo sổ thanh toán `Payment` với mã PO làm reference.
- Khắc phục lỗi đơn `Prepaid` bị chặn xuất kho do thiếu `Payment`, và tránh lỗi `Conflict` do lệch giá khi nhận hàng.
- `PaymentPlanService.CreateForDeliveredOrderAsync`: Bổ sung graceful fallback tự khởi tạo sổ thanh toán nếu chưa có, và tự động đồng bộ giá chốt tránh conflict unrecoverable khi giao hàng.

## 4. Cầu nối tương thích nhận hàng (Confirm Receipt) cho Mobile và Portal
- `DeliveryRequests.cs`: Mở rộng `ConfirmRetailerReceiptRequest` nhận linh hoạt `qrCodes`, `labelCodes` (từ Mobile), và `qrToken` (từ Portal: `portal-demo`).
- `ShipmentService.ConfirmOrderShipmentsReceiptAsync`: Bổ sung xử lý nhận hàng cấp đơn hàng cho các shipment xuất kho theo mô hình mới F96/F61.
- `OrdersController.ConfirmReceipt`: Tự động nhận diện đơn hàng có `Shipment` để phân phối xác nhận, chuyển trạng thái đơn và nhãn QR sang `Delivered`, đóng dấu `DeliveredDate` và hạn nợ `DueDate`, đồng thời trả về cấu trúc JSON tương thích 100% với Mobile (`order`, `pointsEarned`, `pointsPending`) và Portal.

## 5. Tài liệu kiến trúc
- Bổ sung `SYSTEM_FLOW_NOTES.md` mô tả toàn diện luồng 8 chặng, quy tắc xếp chồng khuyến mãi (`Campaign` vs `BatchDiscount`), và nhật ký bản vá.

## Kiểm chứng (Validation)
- **Unit Tests:** `441/441 passed` (Thêm test mới `QuoteServiceTests.cs`, `ConfirmOrderShipmentsReceiptAsync`, và `RunPickingIssueTests.cs`).
- **Integration Tests:** `50/50 passed` (`FSCM.IntegrationTests`).
- **Mobile Flutter Tests:** `53/53 passed` (`order_issue_contract_test.dart`, `id_format_test.dart`, `money_test.dart`).
- **Frontend Portal:** `npm run build` thành công không có lỗi.

