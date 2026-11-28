# Chứng từ nhập, xuất và luân chuyển kho

Màn hình vận hành: `/warehouse-inbound` (nhập), `/warehouse-outbound` (xuất), `/warehouse-transfers` (luân chuyển), `/warehouse-trace` (tra cứu). API: `/api/v1/warehouse-documents`.

## Quy trình

1. **Nhập từ nhà máy:** Nhà máy đóng hàng, in QR và phát hành pallet với kho đích trên Traceability. FSCM lấy danh sách pallet đã xuất qua API tích hợp và chỉ trả hàng gửi tới kho được phân công. Thủ kho chọn pallet trong Hàng chờ nhận; FSCM tạo hoặc mở lại một PNK gắn với pallet đó, tự lấy kho nhận từ manifest, không nhận warehouseId do trình duyệt chọn. Mở PNK và xem danh sách kiểm đếm chưa cộng tồn. Thủ kho đánh dấu từng carton Đạt/Hỏng/Thiếu, ghi lý do và vị trí cách ly khi hỏng, rồi xác nhận nhận: chỉ carton đạt được cộng tồn trong cùng giao dịch. Quét/nhận lại không cộng tồn lần nữa. Pallet đã nhận tại FSCM bị loại khỏi hàng chờ ngay cả khi ACK về Traceability chưa đồng bộ. Hoàn tất chỉ đóng PNK. Không nhập tay SKU/NSX/HSD hoặc sinh QR nhà máy tại kho. Phiếu cũ và API tạo phiếu thủ công được giữ để tương thích; luồng mới dùng endpoint from-pallet.
2. **Luân chuyển:** Admin chọn kho xuất, kho nhận, batch và số carton, phát hành yêu cầu. Hệ thống giữ tồn ngay, tránh bán cùng lượng hàng. Thủ kho nguồn lập PXK từ yêu cầu, quét đúng carton và xác nhận xuất. Tồn nguồn giảm; carton chuyển sang InTransit. Thủ kho đích lập PNK từ PXK đã ghi sổ, quét chính các carton đã xuất rồi xác nhận nhập. Tồn đích tăng; giữ nguyên carton_id, SKU, NSX/HSD và lịch sử batch nguồn.
3. **Xuất nhiều đơn:** Admin chọn nhiều đơn đã có PO/allocation ở cùng kho và phát hành một yêu cầu xuất. Warehouse Keeper chỉ lập **một PXK** từ yêu cầu đó. Bảng tổng hợp gom SKU/batch và tổng carton cần lấy; bảng chi tiết giữ mapping về từng đơn. Quét một carton tự phân vào một dòng đơn còn thiếu, theo batch đã được thuật toán chọn.
4. Khi quét đủ PXK đơn hàng, tất cả phiếu soạn bên trong hoàn tất và PXK chuyển `Prepared`. Xác nhận xuất tổng gọi nghiệp vụ xuất cho các shipment trong **một giao dịch**, tiêu thụ reservation và trừ tồn một lần. Điều kiện thanh toán, thời hạn giao/MLOR và quy tắc mọi kho của đơn đã soạn đủ tiếp tục có hiệu lực. Nếu kho khác chưa đủ, PXK giữ `Prepared` để xuất lại sau; không hoàn tác việc soạn ở kho này.
5. Operator tiếp tục phân xe/tài xế và bắt đầu giao ở phần điều phối giao hàng. PXK tổng không thay thế chứng từ giao theo từng khách hàng.

## Traceability đơn giản

- Một carton vật lý chứa **một SKU, một batch**; pallet nhà máy cũng chỉ chứa một SKU/batch. Đơn vị tồn và phân bổ là carton. Traceability có serial cho từng sản phẩm trong carton; chưa bán/soạn lẻ bên trong carton.
- QR tại nhà máy chứa BID/PID/CID/UID ổn định tùy cấp. QR carton chỉ chứa carton_id ổn định. Mapping batch, SKU, lot, NSX/HSD và chứng từ được tra trong database, không tin dữ liệu tồn kho từ nội dung QR.
- Bảng `WAREHOUSE_DOCUMENT_CARTON` lưu mapping carton với dòng chứng từ và thời điểm/người quét. Dòng chứng từ giữ batch/SKU/đơn ở từng bước; `BATCH_QR_LABEL` và `QR_LABEL_EVENT` tiếp tục dùng cho picking/giao nhận hiện có.
- Ô truy xuất carton trên màn hình cho xem hành trình nhập/xuất trong phạm vi kho được phép truy cập.
- Với hàng tồn có nhãn QR gộp nhiều carton: dùng **Chuẩn bị nhãn một carton cho hàng tồn**, mở nhãn để in và dán lại từng carton. Hệ thống tách nhãn qua nghiệp vụ QR hiện có, giữ lineage và vô hiệu nhãn gộp. Không quét nhãn gộp như một carton.

## Kiểm soát

- Admin phát hành/hủy yêu cầu; chỉ Warehouse Keeper trong phạm vi kho được giao được lập/quét/ghi sổ PXK/PNK.
- Mỗi yêu cầu chỉ có một PXK; mỗi PXK luân chuyển chỉ có một PNK đích. Không gom đơn khác kho vào cùng PXK.
- Nhập nhà máy quét pallet; xuất đơn/luân chuyển quét carton. Quét trùng không cộng lại; sai batch/kho, carton không khả dụng, batch hết hạn/đang khóa, thiếu hoặc vượt carton đều bị từ chối.
- Tạo phiếu dùng Idempotency-Key gắn nội dung và người tạo. Các bước thay đổi dùng version và giao dịch Serializable; unique constraints ngăn trùng chứng từ/carton trong một phiếu.
- Không được xuất riêng từng đơn, hoàn tất phiếu soạn riêng hay duyệt PNK nhà máy qua luồng cũ khi đã gắn chứng từ mới.
- Chỉ hủy yêu cầu chưa lấy carton. Hủy giải phóng tồn giữ luân chuyển; yêu cầu/phiếu con giữ lại trạng thái Cancelled. Sau khi đã lấy carton, chưa cung cấp thao tác đảo phiếu/hoàn tác soạn hàng.
- Chưa hỗ trợ nhận thiếu, nhận hỏng, xuất một phần hoặc nhận vượt. Đối với luân chuyển/xuất đơn phải quét đủ phiếu. Nhập nhà máy chỉ nhận nguyên pallet; PNK ghi nhận các pallet thực nhận và có thể đóng khi đã nhận ít nhất một pallet, pallet còn lại của chuyến có thể nhận ở PNK khác. Chênh lệch trong pallet phải được xử lý trước khi xác nhận. Đây là giới hạn phiên bản này.

## Áp dụng

Build backend và frontend, sau đó áp dụng migration trên môi trường đích theo quy trình triển khai hiện có:

```powershell
dotnet ef database update --project src/FSCM.Persistence --startup-project src/FSCM.API --context AppDbContext
```

Migration `20261112153513_AddWarehouseDocumentsAndCartons` tạo ba bảng chứng từ và thêm mã giao dịch giữ/giải phóng tồn luân chuyển. Chưa áp dụng vào database triển khai trong thay đổi này.

## Kiểm tra

- Frontend: production build, ESLint file mới, 212 tests hiện có.
- Backend: build API/integration tests; 363 parameterless unit/schema facts chạy trực tiếp cùng tiến trình vì môi trường chặn socket của VSTest. Không bao gồm theory/data-driven tests.
- EF: `has-pending-model-changes` không có thay đổi chưa có migration; sinh migration SQL thành công.
- Có bài test PostgreSQL `WarehouseDocumentsPostgresTests` cho nhập nhà máy, quét trùng/sai/thiếu, giữ tồn, xuất/nhập luân chuyển, gom hai đơn, và ghi sổ lặp không trừ tồn hai lần. **Chưa chạy xác minh thành công**: môi trường chặn kết nối localhost PostgreSQL và kênh giao tiếp testhost. Cần chạy trên máy có PostgreSQL dùng database mới có tên `fscm_e2e_...` qua `FSCM_TEST_POSTGRES` trước khi triển khai.

Cập nhật tách DB: batch/pallet/carton/serial gốc nằm trong `fscm_traceability`; các bảng factory trong DB kho là bản sao khi nhận hàng và dữ liệu di sản. Nhận pallet ghi `FACTORY_RECEIPT_SYNC` cùng giao dịch tồn; API worker ACK sang nhà máy có retry. Xem FactoryTraceability.md và dùng runner mới để migrate đủ cả hai context trước khi vận hành.

Cập nhật ứng dụng độc lập: hệ thống nhà máy nằm ở `../FSCM-Traceability` với giao diện riêng. Backend FSCM nhận manifest và ACK qua API gateway có khóa, không đọc DB nhà máy trong runtime. Menu factory ở FSCM mở URL `Traceability:PublicUrl`. Khởi động lại backend/frontend khi chuyển sang bản này.


## Draft PXK before scans

New warehouse requests create Kind Issue, status Open with a PXK code before scanning. Scans assign cartons without dispatching stock. Posting requires all cartons, preserves document ID/code and dispatches atomically. Transfer receipts require a Posted source issue. Legacy Picking documents keep their existing snapshot completion path. No schema migration.

## Factory integration reads and failures

Only the factory request queue loads `factory-pending-dispatches`. The ordinary
inbound queue, transfer arrivals, outbound, transfer and transport screens do not
load factory pending goods. `factory-receipt-groups` is loaded only for the visible
factory receipt history; detail and create screens do not load either list.
These existing REST reads are integration-specific: pending goods come from the
external factory service, and legacy receipt grouping can enrich local history
with factory dispatch metadata. Local transport read models continue to use GraphQL.

The factory HTTP gateway returns ProblemDetails with `service_unavailable` (503)
for connectivity failures, timeouts and unavailable/misconfigured integration,
and `bad_gateway` (502) for invalid JSON or null responses. A factory state
conflict remains `conflict` (409), and an unknown manifest remains `not_found`
(404). Caller cancellation propagates. Responses never expose integration keys
or raw upstream error bodies. Receipt history keeps its existing local fallback
when optional factory enrichment is unavailable. No schema or data migration.

The existing document list hydrates lines, cartons and pallet mappings through
lookups keyed by document/carton, avoiding repeated full-table scans in memory.
Local stock snapshots remain sufficient for outbound, transfer and transport work.
