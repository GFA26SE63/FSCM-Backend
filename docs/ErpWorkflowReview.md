# Rà soát vận hành ERP — 14/11/2026

## Bằng chứng và phạm vi

- Log người dùng cung cấp (`Pasted text(3).txt`, lần chạy 20261113_231249_2b9d3eb3): 36/36 test qua trên PostgreSQL, gồm source HTTP, nhập pallet, QR inventory/ACK, chuyển kho, accept quotation và PXK gom đơn. Đây là pipeline HTTP trong test, không phải kiểm thử toàn bộ giao diện/deployment.
- Phiên rà soát này: chạy lại 215 test frontend; 35 fact backend logic/schema/HTTP qua; backend build qua trong runner; frontend production build và ESLint file sửa qua.
- Chạy lại PostgreSQL bị chặn socket sau DNS; không có kết quả giao dịch mới cho hai sửa đổi dưới đây. Thử mở rộng runner toàn bộ unit test dừng ở restore, không tính là test đã chạy.
- Chưa chạy đầy đủ UI/mobile, payment provider, vận tải và mọi nhánh trả hàng trên deployment. Không kết luận toàn hệ thống đã đạt mức ERP vận hành thực tế.

## Đã sửa

1. `WarehouseWorkService.ValidBatch` dùng ngày nghiệp vụ Việt Nam thay vì UTC, đồng nhất với nhận pallet. Tránh cửa sổ 00:00–06:59 giờ Việt Nam kiểm tra HSD theo ngày hôm trước.
2. Màn chứng từ kho tải lại dữ liệu/version sau thao tác lỗi. Giữ nguyên mã quét và idempotency key để retry sau xung đột hoặc mất phản hồi; tránh lặp lại mãi với version cũ.

## Các điểm cần hoàn thiện trước vận hành thực tế

| Ưu tiên | Tình huống | Hành vi hiện tại / tác động | Nghiệp vụ cần có |
|---|---|---|---|
| P1 | Đã quét carton rồi phát hiện sai/hỏng hoặc cần hủy yêu cầu | CancelRequestAsync chặn khi có carton đã quét; chưa có thao tác hoàn tác trong luồng chứng từ này | Bỏ quét/đổi carton trước ghi sổ, khôi phục picking và trạng thái QR trong một giao dịch, lưu lý do/người xử lý |
| P1 | Pallet thực tế thiếu hoặc hỏng một carton | Luồng nhận luôn nhập nguyên manifest; nếu vẫn quét PID sẽ nhận toàn bộ số lượng trong manifest | Kiểm đếm/chênh lệch, hàng cách ly, chỉ cộng lượng đạt; phần thiếu/hỏng có chứng từ xử lý riêng |
| P1 | PNK/PXK đã ghi sổ nhưng nhập sai | Chưa có đảo phiếu trong luồng warehouse documents | Phiếu đảo tham chiếu chứng từ gốc, kiểm tra giao dịch downstream, bút toán bù và dấu vết kiểm toán |
| P1 | Một đơn trong PXK gom nhiều đơn chưa đủ điều kiện xuất | Toàn PXK ghi sổ nguyên tử; một đơn bị chặn khiến các đơn còn lại cùng phiếu chờ | Hiển thị điều kiện chặn từng đơn trước gom; nghiệp vụ tách phần chưa xuất có kiểm soát |
| P2 | Một đơn lấy từ hai kho nhưng một kho chưa soạn đủ | Readiness chặn xuất ở kho đã sẵn sàng theo chính sách giao đủ hiện tại | Xác nhận chính sách giao đủ hay giao từng phần; nếu từng phần phải theo dõi lượng còn lại/backorder, tránh tự bỏ chốt hiện có |
| P2 | Nhà máy phát hành một mẻ cho nhiều kho | Dispatch hiện phát hành cả mẻ đến một kho | Chứng từ giao nhà máy chọn pallet và kho đích theo chuyến |
| P2 | Admin chưa phát hành yêu cầu dù PO đã đủ điều kiện | Thủ kho chưa tạo PXK trực tiếp từ PO | Hàng đợi PO đủ điều kiện và nhắc việc; quyết định vai trò phát hành lệnh xuất rõ ràng |

## Kiểm thử nghiệm thu tiếp theo

Ưu tiên các nhánh exception thay vì chỉ happy path: hai thủ kho quét đồng thời; mất phản hồi sau commit rồi quét lại; ACK ngừng rồi phục hồi; carton hỏng sau picking; khóa/hết hạn batch giữa lúc soạn và xuất; hủy sau quét một phần; đơn nhiều kho; một đơn bị chặn trong PXK tổng; trả hàng đã giao và đối soát tồn/công nợ. Mỗi kịch bản phải đối soát QR, chứng từ, tồn vật lý, reservation và ledger.

Các thay đổi nghiệp vụ exception cần thiết kế trạng thái và bút toán bù trước khi triển khai. Không xóa lịch sử hoặc sửa trực tiếp số tồn để gỡ phiếu bị kẹt.
