# Nhà máy và traceability

## Phạm vi đã thống nhất

- Mã gọn; SKU, NSX, HSD là thuộc tính riêng, không lặp vào mọi mã con.
- Một carton và một pallet chỉ chứa một SKU/batch.
- Mỗi sản phẩm vật lý có serial duy nhất; FSCM vẫn quản lý số lượng tồn theo carton.
- Mô phỏng nhà máy ở `/factory-traceability`, chỉ Admin được sản xuất/đóng hàng/phát hành chuyến và in nhãn nhà máy. Ứng dụng nhà máy độc lập ở `../FSCM-Traceability`, có API + giao diện riêng, cổng local 5250, Dockerfile riêng và không có ProjectReference đến FSCM. Chỉ dùng DB `fscm_traceability`. FSCM dùng HTTP gateway có khóa tích hợp, giữ chứng từ/tồn và snapshot manifest khi nhận. Xem README của project standalone để deploy riêng.

## Mã và quyền sở hữu

Ví dụ nhà máy F01, line A, ngày sản xuất 27/08/2026, mẻ thứ nhất trong ngày:

| Đối tượng | Ví dụ | Quy tắc |
|---|---|---|
| Batch sản xuất | `BID-F01-20260827-A-0001` | `BID-{nhà máy}-{yyyyMMdd}-{line}-{thứ tự mẻ 4 số}` |
| Pallet | `PID-F01-20260827-A-0001-0001` | Phần định danh batch + thứ tự pallet 4 số trong batch |
| Carton | `CID-F01-20260827-A-0001-0001` | Phần định danh batch + thứ tự carton 4 số trong batch, không reset theo pallet |
| Sản phẩm | `UID-F01-20260827-A-0001-000001` | Phần định danh batch + thứ tự sản phẩm 6 số trong batch, không reset theo carton |
| Mã quản lý tồn | `WH01-SKU001-BID-F01-20260827-A-0001-20260828-20270228` | Kho + SKU + batch gốc + ngày nhập + HSD |

Thứ tự mẻ tăng trên toàn nhà máy/ngày, dùng chung cho 4 line A/B/C/D. Tối đa 9.999 mẻ/ngày/nhà máy, 9.999 carton/mẻ. Không quay vòng khi vượt giới hạn. Bản mô phỏng giới hạn 100 pallet, 100 carton/pallet, 1.000 sản phẩm/carton và 50.000 sản phẩm/mẻ.

Nhà máy sở hữu BID/PID/CID/UID. QR chỉ chứa khóa định danh tương ứng; nhãn in kèm SKU, batch, NSX và HSD. Dùng FK và unique constraint để mapping, không phân tích chuỗi QR để đoán số lượng/ngày/SKU. Không gọi các mã tự đặt này là mã GS1/SSCC.

`FACTORY_PRODUCTION_BATCH` → `FACTORY_PALLET` → `FACTORY_CARTON` → `FACTORY_PRODUCT_UNIT` trong DB traceability là quan hệ đóng gói tại nguồn. Các bảng cùng tên đã có trong FSCM được giữ làm bản sao/di sản; thao tác sản xuất không ghi vào đó. Serial sản phẩm mới chỉ nằm trong DB traceability. Một batch có nhiều pallet, mỗi pallet có nhiều carton, mỗi carton có nhiều sản phẩm. Pallet là đơn vị logistics, không phải bản thân batch. Sản phẩm/SKU là master data đang phát hành từ catalog FSCM; không tạo một catalog giả riêng trong mô phỏng.

`BATCH.FactoryProductionBatchId` giữ tham chiếu đến batch nhà máy; `BATCH.Id` là UUID nội bộ cho thuật toán/tồn, `BATCH.BatchCode` là mã quản lý tồn ở bảng trên. Không phát sinh batch sản xuất mới ở kho. Các lần nhận cùng batch vào cùng kho trong cùng ngày gộp một bản ghi tồn; khác ngày tạo bản ghi riêng. Dùng ngày nghiệp vụ Việt Nam UTC+7.

Chỉ số unique theo SKU/NSX/HSD cũ được giữ với dữ liệu legacy (`FactoryProductionBatchId IS NULL`); dữ liệu mới unique theo kho/batch gốc/ngày nhập. Vì vậy hai mẻ cùng SKU/NSX/HSD không bị gộp sai. Dữ liệu cũ không được tự bịa batch/pallet nhà máy để backfill.

## Chạy quy trình

1. Admin mở `/factory-traceability`, chọn nhà máy, line, SKU đang phát hành, NSX/HSD, số pallet, carton/pallet và sản phẩm/carton. Thao tác mô phỏng tạo batch → serial → carton → pallet trong một giao dịch. Chưa cộng tồn kho.
2. Chọn pallet để xem manifest, in QR pallet và carton. Chọn carton để in QR từng sản phẩm. Mã đã cấp không sửa, không tái sinh tại kho.
3. Phát hành chuyến cho cả batch đến một kho đích đang hoạt động. Trạng thái pallet chuyển `Packed → Dispatched`; không đổi kho đích sau khi phát hành. Thời gian phát hành được giữ trên từng pallet. Phiên bản này chưa có chứng từ chuyến vận tải độc lập hoặc chia một mẻ cho nhiều kho.
4. Warehouse Keeper mở `/warehouse-work`, chọn kho được giao và tạo PNK lý do Nhập từ nhà máy. Phiếu ban đầu rỗng, không nhập SKU/NSX/HSD/số lượng tay.
5. Quét PID. Hệ thống lấy manifest từ module nhà máy, kiểm tra pallet chưa nhận, đã xuất đến đúng kho, batch chưa hết hạn, SKU còn phát hành. Tạo/tra bản ghi tồn; map CID vào nhãn carton hiện có; cộng số carton và ghi ledger, lịch sử QR, người nhận, PNK, thời điểm nhận trong cùng giao dịch Serializable.
6. Quét lại PID trên cùng PNK trả thành công cũ kể cả version cũ hoặc PNK đã đóng, không cộng thêm tồn. Quét PID đã nhận trên PNK khác bị chặn. Sai kho, chưa phát hành, mã CID/UID quét vào ô pallet, batch hết hạn đều không nhận.
7. Tồn có hiệu lực ngay khi quét thành công. Nút Hoàn tất đóng phiếu; không cộng tồn lại. Không cho đóng phiếu rỗng. Nhận nguyên pallet, chưa hỗ trợ thiếu/hỏng bên trong pallet hay đảo giao dịch nhận.
8. Phân bổ FEFO/MLOR dùng batch tồn đã sync. Yêu cầu admin → phiếu xuất tổng nhiều đơn hoặc phiếu luân chuyển → quét CID đúng batch theo allocation. Khi chuyển kho, giữ BID/CID/UID, tạo mã quản lý tồn ở kho đích với ngày nhận mới.
9. Tra BID/PID/CID/UID để xem batch, line, SKU, NSX/HSD, quan hệ đóng gói gốc, serial trong carton, vị trí/trạng thái carton hiện tại và lịch sử chứng từ trong phạm vi kho được phép. Pallet trên màn hình truy xuất là pallet đóng gói ban đầu; sau khi tách carton, vị trí hiện tại phải đọc từ carton, không suy từ pallet gốc.

## Tích hợp và giới hạn

API nhà máy: `/api/v1/factory-traceability`, `POST simulate`, `POST {batchId}/dispatch`, `GET pallets/{code}/manifest`, nhãn in theo pallet/carton, và `GET trace?code=...`. API kho vẫn là `/api/v1/warehouse-documents`: `POST factory-receipts` với kho/ghi chú; `POST {id}/scans` dùng trường `cartonId` hiện hữu để truyền PID đối với PNK nhà máy. Trường giữ tên cũ để không phá request scanner của phiếu xuất/luân chuyển.

Hai ứng dụng có tiến trình và lịch deploy riêng. Traceability sở hữu `FactoryDbContext`; backend FSCM không đăng ký DbContext nguồn hoặc đọc/ghi DB nhà máy trong runtime. Gateway FSCM gọi HTTP `GET /api/integration/pallets/{code}/manifest`, `GET /api/integration/trace` và `POST /api/integration/receipts` với `X-Traceability-Key`. Tồn, chứng từ, lịch sử và `FACTORY_RECEIPT_SYNC` cùng commit trong DB kho; worker gửi ACK qua HTTP mỗi 5 giây và retry khi lỗi. ACK idempotent theo pallet/phiếu. Không cần giao dịch phân tán và không cộng lại tồn nếu ACK lỗi. Quét lại pallet đã nhận dùng cache cục bộ kể cả khi service nhà máy tạm ngừng.

Giao diện nhà máy đăng nhập qua backend của chính nó; backend đó dùng API auth/catalog của FSCM. Token giữ trong cookie mã hóa HttpOnly, không chia sẻ JWT signing secret hay connection string DB kho. Mọi thao tác sản xuất chỉ ghi DB nhà máy. Dữ liệu SKU/kho được kiểm tra qua HTTP trước sản xuất/phát hành. Source trace trả quan hệ đóng gói; FSCM ghép lịch sử tồn/chứng từ tại kho theo quyền người dùng.

Các kiểu DbContext nguồn cũ và công cụ copy trong backend FSCM chỉ còn cho bảo trì/chuyển dữ liệu di sản, không đăng ký trong runtime. Project standalone giữ nguyên ID migration đã áp dụng và cùng schema/ID vật lý; không reset DB hoặc cấp lại mã.

Mã tự đặt hỗ trợ bản mô phỏng nội bộ. Khi tích hợp đối tác dùng GS1, cần GTIN/SSCC được cấp đúng quy tắc; không thêm tiền tố giả để tự nhận tương thích. Nguồn đối chiếu: https://ref.gs1.org/architecture/system-architecture/ và https://ref.gs1.org/ai/.

API tạo goods receipt thủ công cũ trả 409 và hướng dẫn sang PNK quét pallet; `/receipts/new` mở luồng mới. Dữ liệu và các phiếu lịch sử vẫn đọc được. API sinh QR cũ không được sinh nhãn mới cho batch đã sync nhà máy; dùng in lại CID hiện hữu.

## Triển khai và kiểm tra

Áp dụng các migration mới theo thứ tự, gồm `AddWarehouseDocumentsAndCartons`, `AddFactoryTraceability` và migration mở rộng mã snapshot (nếu database chưa có). Không rollback trực tiếp sau khi đã có dữ liệu nhà máy: rollback mất traceability và chỉ số legacy không thể biểu diễn nhiều mẻ trùng ngày.

Build API/frontend và chạy kiểm thử factory identity/schema. `WarehouseDocumentsPostgresTests` kiểm tra mô phỏng, chưa cộng tồn trước nhận, chặn sai kho/chưa xuất, nhận nguyên pallet, quét trùng, hai mẻ trùng ngày, trace serial, luân chuyển giữ batch gốc và xuất tổng hai đơn. Test dùng cặp database mới tên `fscm_e2e_...` và `fscm_e2e_..._trace` qua `FSCM_TEST_POSTGRES`; cần môi trường cho phép kết nối PostgreSQL. Kết quả thực chạy được ghi trong báo cáo bàn giao, không coi test chỉ build là đã chạy giao dịch thành công.

Kiểm thử tách DB có thêm kiểm tra: model traceability chỉ chứa bốn bảng nhà máy, không có FK đến WMS; assembly standalone không tham chiếu runtime FSCM; đăng nhập/phân quyền/khóa tích hợp đúng; sản xuất không tạo serial trong FSCM; nhận tồn trước rồi ACK, replay ACK không tăng tồn. Các workflow PostgreSQL vẫn cần chạy trên máy có kết nối mạng; build hoặc fact chạy trực tiếp không thay thế kết quả giao dịch thực tế.

## Lệnh migration và test từ PowerShell 7

```powershell
& "C:\Users\Thien\Downloads\CapstoneProject\PackageCode\FSCM-Backend\scripts\Migrate-And-TestWarehouse.ps1"
```

Runner kiểm tra cả hai kết nối, build/test schema, migrate từng context vào đúng DB, copy dữ liệu nhà máy cũ sang DB mới nếu có, rồi chạy test bằng hai DB tạm và build/test frontend. Lần đầu sau khi tách DB phải chạy **không có `-SkipMigration`**. Các lần test tiếp theo có thể dùng `-SkipMigration` nếu schema cả hai DB đã cập nhật. Không tự xóa DB test; báo cáo TRX được giữ tại thư mục in cuối output.

Cấu hình runtime: `ConnectionStrings:DefaultConnection` cho FSCM và `ConnectionStrings:TraceabilityConnection` cho nhà máy. Có thể cấp bằng environment `ConnectionStrings__...`; cấu hình cục bộ `appsettings.json` được git-ignore. Không đưa mật khẩu vào lệnh CLI hoặc source đã commit.

Tool `tools/FSCM.TraceabilityMaintenance` có các lệnh `migrate`, `copy-legacy`, `verify`, `sync`. Lệnh copy giữ nguyên BID/PID/CID/UID và tất cả ID, không xóa bảng hay chứng từ cũ, không ghi đè trạng thái gốc đã tồn tại. Bản sao cục bộ không trở thành nguồn cấp mã sản xuất. Nếu hai bên có cùng ID nhưng metadata batch khác nhau, tool dừng và rollback phần copy. Triển khai bản code này sau khi cả hai schema và phần copy hoàn thành, tránh server cũ tiếp tục sinh batch ở DB cũ trong lúc chuyển đổi.

Migrations hiện tại: FSCM dùng chuỗi migration cũ và `AddFactoryReceiptSync`; project standalone giữ `InitialFactoryDatabase` đã áp dụng. Nếu gọi EF trực tiếp: backend dùng `--context AppDbContext`, standalone dùng `--context FactoryDbContext` với `--project ../FSCM-Traceability --startup-project ../FSCM-Traceability`. Không áp dụng model WMS vào DB nhà máy.

## Chạy ứng dụng nhà máy riêng

```powershell
dotnet run --project "C:\Users\Thien\Downloads\CapstoneProject\PackageCode\FSCM-Backend\..\FSCM-Traceability" --launch-profile http
```

Mở http://localhost:5250/. Khởi động lại backend FSCM để nạp gateway HTTP mới. Menu nhà máy trong frontend FSCM mở app riêng qua `Traceability:PublicUrl`; không còn form cấp mã sản xuất trong WMS. Bản deploy dùng container từ thư mục standalone, cấu hình Fscm__BaseUrl, Integration__ApiKey và connection string nhà máy. WMS chỉ cần Traceability__BaseUrl/PublicUrl/ApiKey cho runtime. Cấu hình DB nguồn cũ trong appsettings là cấu hình bảo trì, không cấp cho backend WMS khi deploy mới nếu không chạy tool copy.

Kiểm tra bản tách project: 28 fact logic/schema/HTTP chạy cùng tiến trình đã qua; có test in-process cho auth/key gateway. Test workflow PostgreSQL đã được đổi sang source HTTP pipeline riêng và hai database test; cần chạy lại runner ở PowerShell có mạng để xác minh toàn tuyến sau thay đổi này. Không lấy kết quả PostgreSQL của bản tách DB trước làm kết quả của bản tách ứng dụng.

Nếu màn hình nhà máy báo không kết nối FSCM khi đăng nhập: xác thực Admin đi qua `Fscm:BaseUrl` của project standalone (local mặc định http://localhost:5246/). Chạy `scripts/Start-FactoryStack.ps1` trong PowerShell để kiểm tra health, mở backend còn thiếu và tái sử dụng ứng dụng đã chạy. Script không thay DB, không bỏ kiểm tra chứng chỉ HTTPS, không khởi động trùng nếu cổng đã có ứng dụng. Nếu health lỗi sẽ in nguyên nhân và thư mục log. Khi thấy `BOTH APPS READY`, tải lại http://localhost:5250/ rồi đăng nhập.

Cập nhật xem chi tiết mẻ: dialog mở ngay khi bấm Xem chi tiết, tải metadata theo ID và danh sách mapping từ API có phân trang. Có thể xem carton theo pallet, serial theo carton, tìm mã không phân biệt hoa/thường và lưu URL để mở lại. Không cần migration cho bản cập nhật đọc dữ liệu này. Dùng `scripts/Restart-Traceability.ps1` để dừng đúng instance đang giữ file, build lại và mở dịch vụ, sau đó Ctrl+F5. Kiểm tra đã qua: 5 UI regression và 30 fact logic/schema/HTTP/SQL translation chạy in-process. Workflow PostgreSQL bổ sung kiểm tra số lượng mapping và phân trang, cần chạy ở môi trường có mạng để xác minh thực tế.
