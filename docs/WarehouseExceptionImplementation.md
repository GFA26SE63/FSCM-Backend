# Luồng kho đã thống nhất

## Phiếu soạn và phiếu xuất là hai chứng từ

Yêu cầu xuất → phiếu soạn → xác nhận xuất tạo PXK đã ghi sổ.

- Phiếu soạn giữ mapping carton hiện hành và lịch sử thêm/bỏ/đổi. Soạn hàng không trừ tồn vật lý; reservation theo allocation vẫn giữ nguyên.
- Được bỏ/đổi carton trước khi xác nhận xuất, kể cả phiếu đã đủ số lượng. Bỏ carton làm giảm số đã soạn, mở lại picking và trạng thái sẵn sàng của shipment. Đổi carton phải cùng batch allocation; thay batch cần nghiệp vụ phân bổ lại riêng.
- Đổi carton là một giao dịch: kiểm tra carton mới trước; nếu không hợp lệ, carton cũ vẫn nằm trên phiếu. Không triển khai bằng hai request remove và scan độc lập.
- Xác nhận xuất kiểm tra lại toàn bộ điều kiện, tạo PXK với snapshot mapping, tiêu thụ reservation và trừ tồn trong cùng giao dịch. Replay không tạo PXK hoặc trừ tồn lần hai.
- PXK đã ghi sổ bất biến. Sửa sau xuất qua chứng từ đảo/điều chỉnh hoặc trả hàng phù hợp trạng thái downstream.

## Các ràng buộc phải xử lý cùng lúc

Code hiện tại dùng QR_LABEL_EVENT loại Picked để lấy carton của shipment. Khi bổ sung bỏ quét, không được chỉ xóa dòng WAREHOUSE_DOCUMENT_CARTON: shipment sẽ vẫn đọc event Picked cũ và có thể xuất cả carton đã bỏ. Phải chuyển sang mapping đang hiệu lực, đồng thời giữ nguyên event lịch sử.

Code hiện tại tự Complete picking và tạo shipment Ready khi quét đủ. Bỏ carton sau thời điểm này phải cập nhật cả picking item/list và shipment readiness; không để shipment Ready trong khi phiếu thiếu hàng. Không xóa shipment có tham chiếu downstream.

Idempotency key quét hiện gắn document/label cố định. Phải có operation ID cho từng lần thêm/bỏ/đổi, để quét lại carton đã bỏ là thao tác mới nhưng retry cùng thao tác không ghi nhận hai lần.

## Ngoại lệ còn lại

1. Nhập pallet: kiểm đếm Accepted / Damaged / Missing theo CID; chỉ Accepted vào tồn bán được. Damaged vào khu cách ly và bị khóa; Missing không cộng tồn. ACK truyền kết quả từng carton, không xác nhận toàn pallet là nhận đủ khi có chênh lệch.
2. Đảo phiếu: liên kết chứng từ gốc, lý do, người duyệt; bút toán bù và cập nhật trạng thái QR. Từ chối đảo nếu hàng đã có giao dịch downstream chưa được xử lý. Không xóa chứng từ gốc.
3. PXK gom đơn: cho chọn các đơn đủ điều kiện từ phiếu soạn, phần chưa xuất còn mở. Bỏ unique một child duy nhất nếu phát sinh nhiều PXK; dùng ràng buộc chống xuất trùng từng carton/dòng số lượng.
4. Đơn nhiều kho: mặc định vẫn giao đủ theo cam kết hiện tại. Cho giao từng phần chỉ khi đơn có chính sách cho phép rõ ràng; theo dõi phần còn lại, không tự coi đơn đã hoàn tất.
5. Nhà máy phát hành theo chuyến/pallet: một batch có thể chia pallet cho nhiều kho; pallet đã phát hành không đổi kho đích trực tiếp.
6. Hàng đợi PO đủ điều kiện: hiển thị theo kho, điều kiện thanh toán, số đã phân bổ/soạn/xuất và lý do bị chặn. Chỉ các đơn chưa nằm trong yêu cầu đang hiệu lực mới được phát hành thêm.

## Nghiệm thu bắt buộc

- Bỏ/đổi trước xuất, đổi sai batch rollback giữ carton cũ.
- Quét đủ rồi bỏ một carton: picking và readiness đều không còn hoàn tất.
- Quét A → bỏ A → quét lại A, replay từng thao tác; tồn không đổi trước xuất.
- Shipment không chứa carton đã bỏ, kể cả carton đó được soạn cho đơn khác.
- Hai người cùng sửa/xuất: một giao dịch thắng, giao dịch còn lại trả conflict, không xuất trùng.
- Sau xuất: mọi đường API thêm/bỏ/đổi đều từ chối.
- Nhận thiếu/hỏng không tạo tồn ảo; retry/ACK mất kết nối không nhận lại.
- Đảo phiếu tạo bút toán bù đúng và bị chặn khi có downstream.

## Trạng thái triển khai 14/11/2026

Đã có code API/UI tách Picking và Issue; bỏ/đổi carton trong một transaction; cập nhật lại picking/shipment khi bỏ carton; shipment đọc mapping hiệu lực; migration chuyển Issue Open/Prepared cũ thành Picking. Xác nhận xuất tạo Issue snapshot và ghi sổ trong cùng transaction. Đã bổ sung kịch bản PostgreSQL cho bỏ/quét lại/đổi và chặn sửa sau xuất.

Đã triển khai thêm kiểm đếm pallet theo CID trước khi nhận: Accepted vào tồn bán được; Damaged ghi vào sổ cách ly riêng với CID, lý do và vị trí, không tạo QR khả dụng; Missing không cộng tồn. Biên bản là snapshot bất biến gắn pallet/phiếu nhập, kể cả toàn pallet thiếu/hỏng. Hàng cách ly không được cộng vào InventoryBalance bán được; chưa có nghiệp vụ giải phóng cách ly, trả nhà máy hoặc tiêu hủy. Các tồn này phải được đọc ở bảng kiểm đếm/sổ cách ly của phiếu, không suy ra từ AvailableQuantity.

Outbox lưu và gửi cùng snapshot kiểm đếm; nguồn xác thực đủ danh sách CID và ACK lại payload. Gửi lại kết quả giống nhau không cộng tồn; khác kết quả bị từ chối. Endpoint scans cũ vẫn hỗ trợ nhận đủ toàn pallet để tương thích client trước, còn giao diện mới dùng preview và xác nhận kết quả rõ ràng. Biên bản cũ không được tự gán kết quả kiểm đếm mới.

Nhà máy đã hỗ trợ chọn từng pallet Packed để phát hành tới kho; một batch có thể chia pallet cho nhiều kho. Pallet đã phát hành không đổi kho đích. Giao diện chi tiết mẻ hiển thị kết quả thiếu/hỏng/vị trí cách ly đã nhận qua ACK.

Còn lại: đảo chứng từ, tách phần xuất của phiếu gom đơn, chính sách giao từng phần nhiều kho, hàng đợi PO kèm đầy đủ lý do chặn và xử lý tiếp hàng cách ly. Chưa xác nhận các nghiệp vụ này hoàn tất.

## Kiểm tra và triển khai bản tiếp theo

Backend cùng project integration test đã compile thành công. Frontend build, 217 kiểm thử FSCM và 7 kiểm thử giao diện Traceability qua. Bốn fact mới kiểm tra hợp đồng kiểm đếm đã được bổ sung; kịch bản PostgreSQL có nhận thiếu/hỏng, replay, ACK và chia pallet hai kho. Chưa chạy các kiểm thử backend mới trên PostgreSQL trong môi trường agent; không lấy kết quả triển khai bản tách picking trước đó làm bằng chứng cho bản này.

Chạy scripts/Deploy-WarehousePicking.ps1 bằng PowerShell 7 ở máy có quyền mạng. Script yêu cầu tối thiểu 40 kiểm thử backend trên DB test, sau đó build, dừng đúng hai ứng dụng local, migrate nguồn Traceability trước rồi WMS, restart và health check cả hai. Hai migration dùng DB riêng; nếu bước sau thất bại thì migration đã xong không tự rollback. Không seed tồn ứng dụng và không publish frontend lên hosting ngoài.
