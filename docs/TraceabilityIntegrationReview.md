# Traceability → FSCM → xuất đơn hàng

## Trách nhiệm dữ liệu

Traceability sở hữu BID/PID/CID/UID và quan hệ đóng gói gốc. FSCM sở hữu tồn, reservation, lịch sử QR, phiếu kho và vị trí carton hiện tại. Hai ứng dụng dùng database riêng, trao đổi bằng HTTP có khóa tích hợp. Không sinh lại mã nguồn khi nhập kho.

SKU/kho được nhà máy lấy từ catalog FSCM. Manifest chỉ được lấy khi quét pallet đã phát hành; phát hành tại nhà máy chưa làm tăng tồn FSCM.

## Nhập hàng

1. Admin nhà máy sản xuất/đóng hàng và phát hành pallet đến kho đích.
2. Warehouse Keeper mở `/warehouse-work`, chọn kho, tạo PNK lý do nhập từ nhà máy.
3. Quét PID. FSCM kiểm tra kho đích, trạng thái pallet, SKU và ngày batch, lưu snapshot batch/pallet/carton, tạo nhãn CID và cộng tồn carton trong cùng giao dịch.
4. Quét lặp trên cùng phiếu không cộng lại. Pallet đã nhận trên phiếu khác bị chặn.
5. Hoàn tất PNK chỉ đóng phiếu; tồn đã cộng khi nhận pallet.
6. Cùng giao dịch nhận, FSCM tạo outbox `FACTORY_RECEIPT_SYNC`. Worker gửi ACK mỗi 5 giây và retry; ACK thất bại không làm mất hay cộng lại tồn đã nhận.

Phiên bản này nhận nguyên pallet, chưa có nhận thiếu/hỏng, đảo phiếu hoặc nhận từng carton từ nhà máy.

## Bảng Inventory / QR

Menu **Kho vận → Inventory · QR nhà máy**, URL `/inventory`, API `GET /api/v1/inventory/qr-codes`.

Bảng hiển thị carton đã nhận, pallet gốc, batch nhà máy, batch tồn, SKU, số sản phẩm/carton, NSX/HSD, kho hiện tại, trạng thái QR, phiếu nhập gốc và trạng thái ACK. Có tìm CID/PID/BID/mã tồn/SKU, lọc kho/trạng thái QR/ACK và phân trang 50 dòng. Bấm CID xem lịch sử QR và truy xuất serial; bấm mã tồn xem tồn/reservation theo batch.

Danh sách và lịch sử QR đọc từ DB FSCM; truy xuất serial/đóng gói đầy đủ gọi Traceability. FSCM không sao chép toàn bộ UID vào DB kho. Pallet hiển thị là pallet đóng gói gốc, không đại diện vị trí hiện tại sau khi tách/chuyển carton.

Warehouse Keeper chỉ xem QR ở kho hiện tại được giao; Admin/Operator có quyền đọc toàn hệ thống. Khi đang chuyển/giao, trường kho QR vẫn là kho xuất cho đến bước cập nhật vị trí tiếp theo.

Reservation theo batch, chưa gắn CID cụ thể trước picking. Vì vậy QR `Available` không đồng nghĩa toàn bộ số carton trong batch còn khả dụng cho đơn mới; kiểm tra tồn khả dụng/reserved tại màn batch. Bảng giữ cả mã đã soạn/đang giao/đã giao để truy vết, không dùng tổng số dòng làm tồn vật lý hiện tại.

ACK gồm: đã xác nhận, chờ gửi/thử lại, hoặc dữ liệu legacy không có outbox. Không tự nhận legacy là đã đồng bộ.

## Đơn hàng và xuất kho

Allocation hiện chạy tự động khi tạo đơn và giữ tồn theo SKU/batch/kho. Không yêu cầu thủ kho tự chọn lại batch.

| Mốc | Hành vi |
|---|---|
| Báo giá đang chờ retailer | Chưa có PO, chưa đủ điều kiện lập yêu cầu xuất đơn |
| Accept công nợ hợp lệ | Tạo PO và picking list theo từng kho |
| Accept trả trước | Chờ thanh toán; chỉ tạo PO/picking sau khi ghi nhận trả đủ |
| Có PO/picking chưa bắt đầu soạn | Admin chọn kho và một hoặc nhiều đơn, phát hành yêu cầu xuất |
| Yêu cầu xuất mở | Warehouse Keeper mở yêu cầu, bấm tạo một PXK cho toàn bộ yêu cầu |
| Quét CID | Kiểm tra kho/trạng thái/batch theo allocation; lưu carton về từng dòng đơn |
| Quét đủ | PXK chuyển Prepared; các phiếu soạn hoàn tất |
| Xác nhận xuất | Kiểm tra điều kiện giao/thanh toán và mọi kho đã soạn đủ; tiêu thụ reservation và trừ tồn một lần |
| Giao hàng | Operator phân xe/tài xế và bắt đầu giao theo shipment; không trừ tồn lần nữa |

Một đơn phân từ nhiều kho có phiếu theo từng kho. Có thể gom nhiều đơn cùng kho trong một PXK và vẫn giữ mapping CID → đơn. Phiên bản này Admin phát hành yêu cầu trước, thủ kho tạo PXK từ yêu cầu; retailer accept không tự tạo PXK và thủ kho không lập PXK trực tiếp từ quotation.

## Kiểm thử và đưa vào sử dụng

Bổ sung màn/API inventory này dùng các bảng đã có, không cần migration mới. Phải chạy/deploy backend và frontend mới để xuất hiện menu/API; build test trong thư mục artifacts riêng không thay thế tiến trình đang chạy.

Đã kiểm tra local: build backend/frontend, lint các file frontend thay đổi, 215 test frontend và 34 fact logic/schema/HTTP/SQL translation. Chưa xác minh giao dịch trên Azure trong phiên rà soát này vì DNS từ môi trường lệnh không truy cập được server.

Chạy PowerShell trên máy có quyền truy cập PostgreSQL:

```powershell
& "C:\Users\Thien\Downloads\CapstoneProject\PackageCode\FSCM-Backend\scripts\Test-FactoryIntegration.ps1"
```

Script kiểm tra hai kết nối và khóa tích hợp, build riêng tránh khóa file ứng dụng đang chạy, rồi chạy kiểm thử nhập pallet → QR inventory → ACK → chuyển kho → accept quotation → PXK gom đơn. Chỉ tạo/migrate/seed hai DB test mới `fscm_e2e_integration_...` và `..._trace`, giữ lại DB/TRX; không migrate hay thay tồn ở DB nghiệp vụ. Source HTTP chạy qua pipeline thật trong tiến trình test, chưa thay thế kiểm tra URL/CORS/TLS của bản deploy.
