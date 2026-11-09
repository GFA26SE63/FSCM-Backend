# Azure Blob Storage — Lưu trữ hình ảnh FSCM

Hệ thống lưu ảnh nghiệp vụ trong Azure Blob Storage (container Private). PostgreSQL chỉ lưu
**blob key + metadata**, không lưu bytes. Giai đoạn này chỉ hỗ trợ ảnh JPG/JPEG/PNG/WebP (tối đa 10 MB),
chưa hỗ trợ PDF/Word/Excel.

Bốn nhóm ảnh và container tương ứng:

| Nghiệp vụ | Container | Lưu ở đâu trong DB |
|---|---|---|
| Claim / Complaint (bằng chứng khiếu nại, nhiều ảnh) | `claim-evidence` | bảng `COMPLAINT_EVIDENCE.BlobKey` |
| Write-off (ảnh hàng hư hỏng / giảm tồn) | `write-off-attachments` | `WRITE_OFF_ATTACHMENT.BlobUrl` (lưu key) |
| Retailer (ảnh mặt trước cửa hàng) | `retailer-storefront-images` | `RETAILER.StorefrontImageBlobKey` |
| Product (ảnh sản phẩm) | `product-images` | `PRODUCT.ImageBlobKey` |

## 1. Tạo Storage Account và container
1. Tạo **Storage Account** loại StorageV2 (Standard, LRS cho dev).
2. Tạo 4 container ở trên. Mức truy cập để **Private** (không cho ẩn danh).
3. Backend tự tạo container nếu chưa có khi upload lần đầu (`CreateIfNotExists`, PublicAccessType.None).

## 2. Cấu hình kết nối
Backend dùng **connection string** của Storage Account, section `FscmBlobStorage`.

```json
"FscmBlobStorage": {
  "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
  "ClaimEvidenceContainer": "claim-evidence",
  "WriteOffAttachmentsContainer": "write-off-attachments",
  "RetailerStorefrontContainer": "retailer-storefront-images",
  "ProductImagesContainer": "product-images",
  "ReadSasExpiryMinutes": 60
}
```

Local: lưu connection string trong **.NET User Secrets**, không để trong source:
```powershell
cd src/FSCM.API
dotnet user-secrets set "FscmBlobStorage:ConnectionString" "<connection string>"
```
Hoặc dùng biến môi trường (quy ước `__`):
```powershell
$env:FscmBlobStorage__ConnectionString = '<connection string>'
```
Deploy: đặt `FscmBlobStorage__ConnectionString` trong Azure App Service Environment Variables (hoặc
Key Vault reference). Connection string cần **account key** để backend sinh SAS URL đọc ảnh.

## 3. Luồng hoạt động
- Client upload ảnh qua `multipart/form-data` (field `file`). Backend kiểm tra:
  loại ảnh (JPG/JPEG/PNG/WebP), dung lượng (≤ 10 MB), và **magic number** khớp content-type
  (chống đổi tên tệp).
- Backend upload lên container tương ứng, sinh blob key dạng `{nhóm}/{guid}{đuôi}`, lưu key + metadata
  vào PostgreSQL. Bytes không vào DB.
- Khi đọc, backend kiểm tra quyền rồi trả **SAS URL chỉ-đọc** hết hạn sau `ReadSasExpiryMinutes` phút.
- Upload ảnh **không** tự thay đổi trạng thái nghiệp vụ: không đổi kết quả khiếu nại, không đổi tồn kho,
  không là điều kiện kích hoạt Retailer, không ảnh hưởng SKU/giá/batch/allocation.

## 4. API
| Method & route | Vai trò | Mô tả |
|---|---|---|
| `POST /api/v1/complaints/{id}/evidence` (file) | Retailer | Bổ sung 1 ảnh bằng chứng; được phép khi khiếu nại chưa có kết quả (kể cả đang xem xét). Nhiều ảnh. |
| `GET /api/v1/complaints/{id}/evidence` | Admin/Operator/Retailer | Danh sách ảnh (Retailer chỉ xem khiếu nại của mình); trả SAS URL. |
| `POST /api/v1/write-offs/{id}/attachments` (file) | Admin/Operator/WarehouseKeeper | Đính kèm ảnh write-off. |
| `GET /api/v1/write-offs/{id}/attachments` | Admin/Operator/WarehouseKeeper | Danh sách ảnh write-off. |
| `DELETE /api/v1/write-offs/{id}/attachments/{attachmentId}` | Admin/Operator | Xóa ảnh write-off (xóa cả blob). |
| `POST /api/v1/retailers/{id}/storefront-image` (file) | Admin/Operator/SalesManager/Sales/Retailer | Upload/thay ảnh mặt tiền. Retailer chỉ sửa của chính mình. Trả `RetailerDetails.storefrontImageUrl`. |
| `POST /api/v1/products/{id}/image` (file) | Admin | Upload/thay ảnh sản phẩm. Trả `ProductResult.imageUrl`. |

Các response có URL ảnh (`storefrontImageUrl`, `imageUrl`, `complaint evidence url`) đều là **SAS URL
ngắn hạn**, sinh lại mỗi lần đọc.

## 5. Lỗi thường gặp
- `503 service_unavailable`: chưa cấu hình `FscmBlobStorage:ConnectionString`.
- `400 validation_error`: sai định dạng ảnh, quá 10 MB, hoặc nội dung không khớp content-type.
- `403 forbidden`: Retailer cố sửa ảnh của cửa hàng khác.
- Không sinh được SAS: connection string thiếu account key (ví dụ dùng SAS-only), backend không tạo
  được SAS đọc — hãy dùng connection string có account key, hoặc chuyển sang user-delegation SAS với
  managed identity (ngoài phạm vi hiện tại).

## 6. Migration
Cột/bảng mới nằm trong migration `AddBlobImageStorage` (thêm `RETAILER.StorefrontImageBlobKey`,
`PRODUCT.ImageBlobKey`, bảng `COMPLAINT_EVIDENCE`). Áp dụng:
```powershell
dotnet ef database update --project src/FSCM.Persistence/FSCM.Persistence.csproj --startup-project src/FSCM.API/FSCM.API.csproj --context AppDbContext
```

## 7. Ngoài phạm vi
Chưa triển khai: Sales Order Attachments, Promotion Images, PaymentReceipt/Refund evidence, Retailer
Documents, và các định dạng tài liệu (PDF/Office). QR hàng hóa **không** dùng Blob Storage — vẫn render
động bằng QRCoder, không có container `qr-labels`.
