# Kiểm tra và benchmark allocation

Chạy các lệnh dưới đây từ thư mục `FSCM-Backend`, bằng .NET 10. Kết quả mặc định nằm trong `artifacts/allocation-benchmark/` (đã được Git bỏ qua). Chạy bản Release khi so sánh thời gian.

## Các lỗi đã sửa

- Sau lỗi serialization PostgreSQL, execution strategy phải bỏ trạng thái EF của lần đã rollback trước khi đọc lại đơn/tồn. Ca fault injection `40001` trên PostgreSQL thật từng trả đơn vẫn ở `Allocating`; sau sửa, chỉ một allocation run và một báo giá được ghi nhận.
- MLOR có cả tỷ lệ và số giờ tối thiểu: dùng `max(tỷ lệ × tuổi thọ SKU, số giờ tối thiểu)` tại ETA. Điều kiện này áp dụng cho kiểm tra khả dụng, allocation real-time và kiểm tra lại trước thanh toán/xuất kho.
- HSD dạng ngày có hiệu lực hết ngày Việt Nam. Ví dụ HSD 12/11 kết thúc lúc 00:00 ngày 13/11 UTC+7, tương ứng 17:00 ngày 12/11 UTC. Chọn batch và tính giá dùng cùng mốc; các báo giá đã lưu vẫn giữ snapshot giá cũ.
- Trọng số không âm, tổng **đúng 1**. Không cho lưu `0.3333 + 0.3333 + 0.3333` rồi để engine thất bại sau đó.
- FEFO đối chứng cũng phải đủ toàn đơn. Đơn thiếu hàng không được tính lượng lấy dở thành hàng đã cấp hoặc lợi ích đẩy hàng cận hạn.
- Khi điểm bằng nhau, ưu tiên HSD sớm hơn trước mã batch; từ chối batch ID trùng hoặc thiếu ngày đến đi kèm thời điểm ETA.

Không cần migration. Các sửa đổi không tự thay trọng số đã công bố trong DB.

## 1. Tạo dữ liệu lặp lại được và quét trọng số

```powershell
dotnet run --project tools/FSCM.AllocationBenchmarks -c Release -- --generate artifacts/allocation-benchmark/scenario.json --run artifacts/allocation-benchmark/scenario.json --grid-step 10 --repetitions 10
```

Mặc định có 60 đơn, 54 batch, nhiều SKU/kho/điểm bán, tuyến không phục vụ được, thiếu hàng và đơn nhiều dòng phải hoàn trả tồn tạm khi thất bại. Seed mặc định là 83. Có thể đổi `--seed 84 --orders 100` khi tạo file khác để kiểm tra khả năng tổng quát, thay vì chỉ chọn bộ trọng số tốt trên một seed.

`--grid-step 10` quét bộ `(expiry, distance, coverage)` cách nhau 0.1, tổng bằng 1; kèm trọng số công bố trong snapshot, các preset và FEFO. Bán kính và mọi dữ liệu đầu vào được giữ nguyên giữa các lượt.

Thử riêng một bộ:

```powershell
dotnet run --project tools/FSCM.AllocationBenchmarks -c Release -- --run artifacts/allocation-benchmark/scenario.json --weights "0.5,0.3,0.2" --repetitions 10 --output artifacts/allocation-benchmark/custom
```

## 2. Lấy snapshot tồn kho PostgreSQL hiện tại

Ví dụ một **đơn giả định mới**, 50 thùng SKU001 cho điểm bán 10004127:

```powershell
dotnet run --project tools/FSCM.AllocationBenchmarks -c Release -- --snapshot artifacts/allocation-benchmark/current.json --retailer 10004127 --sku SKU001 --quantity 50
dotnet run --project tools/FSCM.AllocationBenchmarks -c Release -- --run artifacts/allocation-benchmark/current.json --grid-step 10 --repetitions 10 --output artifacts/allocation-benchmark/current-results
```

Kết nối lấy từ biến môi trường `FSCM_BENCHMARK_CONNECTION`, hoặc `src/FSCM.API/appsettings.json`; có thể chỉ định file khác bằng `--config`. Không ghi mật khẩu vào lệnh hoặc commit file cấu hình. Snapshot mở kết nối PostgreSQL ở chế độ read-only, đọc ETA từ cache, không gọi nhà cung cấp bản đồ để refresh tuyến, không tạo SO/giữ tồn/đổi trọng số. Tuyến cache thiếu/hết hạn sẽ không khả dụng. Snapshot không chứa chuỗi kết nối hoặc thông tin đăng nhập.

File này dùng **tồn còn tự do hiện tại** sau giữ hàng/khóa tồn, không phải tồn trước những đơn đã xử lý trong quá khứ. Chạy lại cùng file dùng đúng cùng dữ liệu kể cả DB sau đó thay đổi; lấy snapshot mới để xem tình hình mới. Snapshot một SKU/một điểm bán không đủ để quyết định trọng số cho toàn hệ thống. Có thể chỉnh danh sách đơn/dòng/tuyến trong JSON để xây dựng tình huống nhiều đơn, giữ mỗi order/line/batch ID duy nhất.

## 3. Đo tải của phần lập phương án

```powershell
dotnet run --project tools/FSCM.AllocationBenchmarks -c Release -- --generate artifacts/allocation-benchmark/stress.json --stress-batches 200 --run artifacts/allocation-benchmark/stress.json --repetitions 5 --output artifacts/allocation-benchmark/stress-results
```

Đây là tình huống cần lấy hết nhiều batch. Tăng lần lượt 200, 400, 800 để quan sát thời gian, bộ nhớ và số dòng trace. Engine chấm lại các batch còn lại mỗi lần lấy, đồng thời lưu trace xếp hạng, nên trường hợp lấy N batch có thể tạo khoảng N(N+1)/2 dòng score trace. Công cụ giới hạn tối đa 2.000 batch stress.

## Đọc kết quả và chọn trọng số

`results.json` ghi hash SHA-256 của đầu vào, môi trường .NET, số lần lặp, ghi chú và từng bộ trọng số. `results.csv` mở được bằng Excel.

| Chỉ số | Cách hiểu |
| --- | --- |
| `fulfilledOrders / totalOrders` | Đủ toàn bộ các dòng của đơn mới được tính thành công |
| `committedQuantity`, `unfulfilledDemand` | Tổng thùng cấp thành công và nhu cầu chưa được cấp; đơn thất bại hoàn trả toàn bộ tồn tạm |
| `nearExpiryQuantity` | Lượng cấp có thời gian sống còn lại tại ETA không vượt `NearDateThresholdHours` của SKU; khác thống kê cận hạn theo ngày trên dashboard |
| `warehouseLegs`, `splitOrders` | Số cặp đơn–kho và số đơn dùng nhiều kho |
| `routeKilometers` | Tổng quãng đường một chiều cho từng cặp đơn–kho; chưa tính gom chuyến nhiều điểm, tải xe hoặc đường quay về |
| `cartonKilometers` | Tổng thùng × km, không phải chi phí vận tải thực tế |
| `medianMilliseconds`, `p95Milliseconds` | Thời gian lập phương án trong bộ nhớ sau warmup, có tạo trace và tính metrics; không gồm DB, API, bản đồ, tính giá hay cạnh tranh giao dịch |
| `medianAllocatedBytes`, `scoreTraceRows` | Bộ nhớ cấp phát trên luồng chạy và số dòng score trace |
| `pareto` | Chỉ xét các bộ đạt số đơn đủ hàng cao nhất; loại bộ bị bộ khác lấn át về lượng cấp, hàng cận hạn, số chặng và km. Đây không phải lựa chọn trọng số tự động |

FEFO đối chứng dùng bộ lọc production để bảo đảm cùng điều kiện. Do helper cũng chạy engine weighted để lấy eligibility, **không báo thời gian FEFO** như thể đó là thời gian của một triển khai FEFO độc lập.

Với mục tiêu cân bằng, ưu tiên giữ mức phục vụ, sau đó so các bộ Pareto theo hàng cận hạn, số kho và km trên nhiều seed/snapshot. Không so điểm weighted giữa hai bộ trọng số khác nhau để kết luận bộ nào tốt hơn. `coverage` là độ phủ của **một batch đối với phần nhu cầu còn thiếu**, không trực tiếp tối ưu số kho; đơn nhiều SKU cũng chưa có bước tối ưu tập kho chung. Weighted greedy là ưu tiên FEFO mềm, không bảo đảm tối ưu toàn cục hoặc FIFO giữa các request chạy đồng thời.

Nếu MLOR của SKU là 42 giờ còn ngưỡng cận hạn là 24 giờ, hàng hợp lệ tại ETA không thể rơi vào nhóm cận hạn 24 giờ. Tăng trọng số expiry không vượt qua được điều kiện MLOR. Các benchmark không tự hạ MLOR để cải thiện kết quả.

Sau khi lựa chọn và kiểm tra, Admin có thể công bố trọng số qua **Cấu hình hệ thống → Trọng số phân bổ đơn hàng**, hoặc `PUT /api/v1/configurations/allocation.weights` với `{ "value": { "expiry": 0.5, "distance": 0.3, "coverage": 0.2 } }`. Việc công bố có hiệu lực cho lần allocation tiếp theo; lệnh benchmark chỉ thử nghiệm offline.
