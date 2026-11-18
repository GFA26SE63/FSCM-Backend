# Nhận pallet bằng điện thoại

1. Nhà máy phát hành pallet và chỉ định kho đích trên Traceability.
2. Thủ kho mở Nhập kho → Hàng chờ nhận → Chọn pallet. FSCM tạo hoặc mở lại phiếu PNK gắn với pallet và kho đích; chưa cộng tồn.
3. Trên điện thoại, mở phiếu và chọn **Quét QR pallet bằng camera**. Cấp quyền camera cho website HTTPS. Quét QR pallet thực tế, không dùng QR carton/sản phẩm.
4. FSCM kiểm tra quyền kho, phiếu đang mở, pallet đúng phiếu, đã xuất đến đúng kho và chưa nhận. Lưu sự kiện `ReceiptPalletScanned` trong AUDIT_LOG gồm mã pallet, người quét, thời gian máy chủ, phiên bản phiếu và thông tin trình duyệt. Quét không cộng tồn.
5. Thủ kho kiểm đếm carton, đánh dấu Đạt / Hỏng cách ly / Thiếu; hàng hỏng bắt buộc có lý do và vị trí cách ly, hàng thiếu bắt buộc có lý do.
6. **Xác nhận kết quả và nhận pallet** gửi ScanId cùng phiên bản phiếu. Máy chủ kiểm tra lần quét thuộc đúng phiếu, đúng người, đúng pallet. Chỉ carton đạt cộng tồn. Ghi sự kiện `ReceiptPalletConfirmed` và biên bản kiểm đếm trong cùng transaction với nhập tồn. Gửi kết quả về Traceability qua cơ chế ACK hiện hữu.
7. **Hoàn tất phiếu nhập** đóng phiếu, không cộng tồn lần nữa. **In / Lưu PDF phiếu nhập kho** xuất số phiếu, kho, người lập, ngày lập/hoàn tất, hàng thực nhập, biên bản và lịch sử quét.

## API

- POST `/api/v1/warehouse-documents/{id}/pallet-scans`: `{code, expectedVersion, source:"Camera"}`; trả scan id, phiên bản mới và manifest.
- GET cùng đường dẫn: lịch sử quét đã xác thực theo quyền kho.
- POST `/{id}/receive-pallet`: bắt buộc ScanId phù hợp cùng kết quả kiểm đếm. Gửi lặp cùng kết quả không cộng tồn hai lần; kết quả khác bị từ chối.
- GET `/{id}/receipt-print`: bản HTML có thể in/lưu PDF, dùng xác thực và quyền kho như phiếu.
- POST `/{id}/scans` không còn tự nhận pallet nhà máy. Client cũ phải dùng quy trình quét → kiểm đếm → xác nhận.

Không có thay đổi schema. Bản frontend mới cần backend mới trước khi triển khai. Kiểm thử camera vật lý trên điện thoại cần thực hiện với HTTPS và QR pallet thực tế; kiểm thử tự động kiểm tra thông điệp gửi từ bộ giải mã và các ràng buộc máy chủ, không thay thế việc kiểm tra camera thiết bị.
