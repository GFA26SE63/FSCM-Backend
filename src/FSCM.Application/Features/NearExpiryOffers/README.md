# F98 – Đặt ưu đãi cận hạn và xác nhận MLOR (hợp đồng đã chốt)

Nguồn: `FSCM_Thuat_toan_FEFO_theo_dot_12h_v3.docx` mục 4.7, các bước S01, S02, S05, S07, S14, S16;
bản đăng ký chốt thiết kế V6. Tài liệu này là **hợp đồng dữ liệu dùng chung** cho F27, F30, F46 và
F33 – sửa ở đây trước khi sửa code hai phía.

MLOR = Minimum Life on Receipt: tỷ lệ hạn sử dụng còn lại tối thiểu khách chấp nhận khi **nhận
hàng**, tính theo tổng hạn sử dụng của sản phẩm.

## 1. Ranh giới sở hữu

| Phần | Chủ | Ghi chú |
| --- | --- | --- |
| Cột §4.7 trên `SALES_ORDER_LINE`, module validation, `INearExpiryOfferCatalog` | F98 – Nhân | Module này |
| Ghi đơn online, `RequestedDeliveryDate` | F27 – Hải | Ghi nguyên văn `AcceptedOrderLine` |
| Đồng bộ đơn offline | F30 – Khiết | Gọi cùng module, `at` = `ServerReceivedAt` |
| Dữ liệu ưu đãi: batch, giá, hiệu lực, version, MLOR riêng, xác nhận | F46 – Khiết | Hiện thực `INearExpiryOfferCatalog` |
| MLOR retailer–SKU, `mlor.default_ratio` | F22 / BE-03 – Hải | Đã có trong `WarehouseConfiguration` |
| Lọc batch theo MLOR hiệu lực, trace | F33 / F35 – Nhân | Chỉ **đọc**, không tính lại tỷ lệ |
| Giá chính thức sau phân bổ | F89 – Nhân | Không vượt `AcceptedNetUnitPrice` |

## 2. Cột trên `SALES_ORDER_LINE`

| Cột | Kiểu | Dòng thường | Dòng ưu đãi |
| --- | --- | --- | --- |
| `LineType` | varchar(30) | `Standard` | `NearExpiryOffer` |
| `NearExpiryOfferId` | uuid? | null | bắt buộc |
| `NearExpiryOfferVersion` | int? | null | bắt buộc |
| `AcceptedNetUnitPrice` | numeric(18,2)? | null | bắt buộc – giá khách đã đồng ý |
| `EffectiveMlorRatio` | numeric(5,4) | bắt buộc | bắt buộc |
| `MlorSource` | varchar(30) | `RetailerProduct` / `ProductDefault` | `NearExpiryOffer` |
| `DefaultMlorRatio` | numeric(5,4)? | = `EffectiveMlorRatio` | MLOR retailer–SKU lúc chấp nhận |
| `AcceptedByUserId` | uuid? | null | Sales ghi nhận xác nhận |
| `AcceptedAt` | timestamptz? | null | bắt buộc |

`(SalesOrderId, LineNumber)` của F27 chỉ bảo đảm các dòng được **đánh số** khác nhau. Thêm hai
unique index có filter để các dòng khác nhau về **nội dung**: `(SalesOrderId, ProductId)` khi
`LineType = 'Standard'`, và `(SalesOrderId, ProductId, NearExpiryOfferId, NearExpiryOfferVersion)`
khi `LineType = 'NearExpiryOffer'`. Cùng SKU khác loại hoặc khác version luôn là hai dòng.

Phân biệt với hai cột sẵn có của F27: `DiscountSourceType` / `DiscountSourceCode` ghi **giảm giá đến
từ đâu** (snapshot mã khuyến mãi để tính tiền). `LineType` + `NearExpiryOfferId` + version ghi
**khách đặt loại hàng gì và theo điều kiện nào**, và là thứ quyết định MLOR hiệu lực cùng việc dòng
đó có bị giới hạn batch hay không. Một dòng `Standard` vẫn có thể mang `DiscountSourceType` của một
chiến dịch KM. Không mã hoá trùng ý nghĩa giữa hai nhóm cột này.

Dòng tạo trước khi contract này tồn tại được backfill `Standard` / `ProductDefault` / `0.25` trong
migration rồi **bỏ default**, nên dòng mới buộc phải khai báo tường minh. Tỷ lệ của các dòng cũ là
giá trị dựng lại, không phải điều kiện khách đã xác nhận.

## 3. Xác định MLOR hiệu lực (§4.7.1)

- **Dòng thường**, kể cả khi có KM theo SKU / nhóm: lấy `RetailerProductMlor.MinimumLifeRatio`
  đang active → `MlorSource = RetailerProduct`. Không có thì lấy `mlor.default_ratio` →
  `MlorSource = ProductDefault`. **KM thông thường không đổi MLOR.**
- **Dòng ưu đãi cận hạn**: lấy `MlorRatio` của đúng version khách đã chấp nhận →
  `MlorSource = NearExpiryOffer`. Mức này chỉ áp cho dòng đó, không lan sang dòng khác hay đơn sau.
- Mọi tỷ lệ phải thỏa `0 < ratio < 1`.
- **Không** suy MLOR từ giá, tỷ lệ giảm, hay hạn còn lại của batch.

## 4. Kiểm tra tại thời điểm nhận (§4.7.2)

Đơn vị chốt là **giờ**, theo `Product.ShelfLifeHours`:

```
EXP − ETA ≥ EffectiveMlorRatio × Product.ShelfLifeHours
% hạn còn tại ETA = (EXP − ETA) / Product.ShelfLifeHours × 100
```

ETA phải tính cả chờ đợt 12h, chờ duyệt, soạn, buffer, lịch chuyến và vận chuyển (F56); duyệt chậm
thì tính lại. "Còn 70% hạn" khác "đã trôi qua 70% hạn".

> `AllocationEngine` hiện tính theo `DateOnly` / ngày. F33 phải chuyển sang giờ để khớp
> `ShelfLifeHours` và `EffectiveMlorRatio`.

## 5. Quy tắc validation (dùng chung cho tạo đơn và đồng bộ)

Dòng thường: `NearExpiryOfferId`, `NearExpiryOfferVersion`, `AcceptedNetUnitPrice`,
`AcceptedMlorRatio`, `AcceptedByUserId`, `AcceptedAt` phải rỗng.

Dòng ưu đãi – thiếu hoặc lệch bất kỳ điều kiện nào thì **từ chối dòng đó**:

1. Đủ sáu trường trên và `CustomerAccepted = true`. Thiếu xác nhận → `validation_error`.
2. Version tồn tại, `IsConfirmed = true`, `IsActive = true`.
3. `ServerReceivedAt ∈ [StartAt, EndAt]` của version đó.
4. `OfferView.ProductId == line.ProductId`.
5. `AcceptedNetUnitPrice == OfferView.NetUnitPrice`.
6. `AcceptedMlorRatio == OfferView.MlorRatio`.

Khi từ chối: **không** tự hạ dòng ưu đãi thành dòng thường, không tự áp tỷ lệ thấp hơn, không tự
đổi giá. Sales phải bổ sung xác nhận hoặc bỏ lựa chọn ưu đãi.

`EligibleBatchIds` rỗng **không** phải lý do từ chối lúc tạo / sync: thiếu hàng chỉ xét trong đợt
12h (S13). Mọi giá và lượng lúc này là tham khảo, chưa giữ hàng.

## 6. Hạn nhận – dùng `SALES_ORDER.RequestedDeliveryDate`

Cột đã có sẵn từ F27, kiểu `DateOnly?`. **Giữ nguyên, không thêm cột timestamp.** Lý do: hạn nhận là
cam kết dịch vụ với retailer ("giao trước ngày 25"), không phải ràng buộc hạn sử dụng. Phép kiểm
MLOR dùng `EstimatedArrivalAt` (`timestamptz`) với `ShelfLifeHours` nên đã đủ chính xác tới giờ mà
không cần cột này chính xác tới giờ. Đổi sang `timestamptz` sẽ buộc F27 bịa ra một mốc giờ, và nếu
bịa 00:00 thì ETA 09:00 cùng ngày sẽ trượt hạn nhận → đơn bị đánh thiếu hàng oan.

Hai quy tắc bắt buộc khi so sánh ở S07:

1. **So theo giờ Việt Nam**: `ETA.ToOffset(+07:00).Date <= RequestedDeliveryDate`. `EstimatedArrivalAt`
   lưu UTC; lấy `.Date` trên UTC sẽ làm mọi ETA trong khoảng 00:00–07:00 giờ VN **lệch một ngày** và
   đơn bị đánh thiếu oan.
2. **Null = không có hạn nhận** → bỏ qua điều kiện hạn nhận cho dòng đó. Không coi là "hôm nay",
   không throw.

Đánh đổi đã chấp nhận: so theo ngày nới hơn so theo giờ (đơn tới 23:00 vẫn qua hạn nhận trong ngày).
Đây là chất lượng dịch vụ, không phải sai đúng, và v3 mục 4.6 đã nói MVP không bảo đảm tối ưu.

## 7. Phần còn lại của F98 (S2)

Đã chốt ở sprint 1: schema, contract, hai port, quy tắc. Còn lại: `OrderLineAcceptanceService`
thực thi mục 3–5, hiện thực `INearExpiryOfferStore`, endpoint đọc ưu đãi cho app, và unit test các
biên ở F97 (68% so với 80% / 60%, hết hiệu lực, thiếu xác nhận, không tự hạ MLOR).
