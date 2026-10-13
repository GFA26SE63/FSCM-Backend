# FSCM Backend – ASP.NET Core 8 modular monolith

FA26SE083 · FSCM – Short Shelf-Life FMCG Supply Chain Management and Distribution Coordination System with FEFO Optimization · nhóm GFA26SE63.

Một solution `FSCM.sln`, **11 module nghiệp vụ**, mỗi module chia **Clean Architecture** (Domain → Application → Infrastructure / Api) và có **schema PostgreSQL riêng** trong cùng một database. Module không đọc bảng của nhau: gọi đồng bộ qua interface ở project `Contracts`, bất đồng bộ qua **integration event + transactional outbox**.

## 1. Cấu trúc

```
fscm-backend/
├─ FSCM.sln
├─ Directory.Build.props        net8.0, nullable, implicit usings (chung mọi project)
├─ Directory.Packages.props     version NuGet tập trung
├─ docker-compose.yml           PostgreSQL 16 cho máy dev
├─ src/
│  ├─ BuildingBlocks/
│  │  ├─ FSCM.SharedKernel                 Entity, AggregateRoot, BusinessRuleException/Guard, Result, IdFormat (quy tắc mã 06/10), DateMath
│  │  ├─ FSCM.BuildingBlocks.Application   IClock, ICurrentUser, IUnitOfWork, ISequenceGenerator + IdGenerator, IEventBus, IScheduledJob, IModuleInstaller, paging
│  │  └─ FSCM.BuildingBlocks.Infrastructure ModuleDbContext (outbox + optimistic locking), OutboxDispatcher, InProcessEventBus, DailyJobScheduler, PostgresSequenceGenerator
│  ├─ Modules/<Module>/
│  │  ├─ FSCM.Modules.<Module>.Domain          entity, value object, quy tắc nghiệp vụ, thuật toán thuần (không I/O)
│  │  ├─ FSCM.Modules.<Module>.Application     use case / service, port (repository interface), event handler, job
│  │  ├─ FSCM.Modules.<Module>.Contracts       interface + DTO + integration event module khác được phép dùng
│  │  ├─ FSCM.Modules.<Module>.Infrastructure  EF Core DbContext (schema riêng), repository, <Module>ModuleInstaller
│  │  └─ FSCM.Modules.<Module>.Api             controller REST /api/v1/...
│  └─ Hosts/
│     ├─ FSCM.Api       Web API: đăng ký mọi module (ModuleCatalog), auth Entra ID / Dev, Swagger, ProblemDetails, CORS Portal
│     └─ FSCM.Worker    outbox dispatcher + job hằng ngày (date tracker, luân chuyển, quá hạn công nợ…)
├─ tests/FSCM.UnitTests  xUnit: phân bổ, luân chuyển, mã ID, bảng vận chuyển
└─ tools/FSCM.AlgoDemo   console in trace 2 ví dụ trong tài liệu (không cần NuGet)
```

Quy tắc phụ thuộc: `Domain` ← `Application` ← `Infrastructure`/`Api`. `Application` của module A chỉ được tham chiếu `Contracts` của module B, không bao giờ `Domain`/`Infrastructure` của B.

## 2. Module

| Module | Schema | Nội dung chính |
|---|---|---|
| Identity | identity | AppUser (gắn Entra object id), vai trò, gán kho / khu vực / retailer, đăng ký thiết bị push, `ICurrentUser`. Admin chỉ được seed, không tạo từ UI |
| Catalog | catalog | Danh mục `CAT-00`, sản phẩm `SKU000` Nháp → Publish → Inactive (đã publish thì không sửa, muốn đổi thì clone), SL, giá, giá vốn, MLOR mặc định |
| Partners | partners | Khu vực, nhóm Sales `Q12-A`, retailer 8 số + thông tin tiềm năng, yêu cầu đăng ký `KB-0000`, MLOR theo retailer–SKU, kỳ hạn công nợ cho phép |
| Logistics | logistics | **Bảng vận chuyển dùng chung** (CR-02, có version theo ngày hiệu lực), ma trận khoảng cách kho↔retailer / kho↔kho, Dmax 20/40 km, ETA, xe & người giao (`NG-000`, biển số) |
| Inventory | inventory | Kho `WH-XX`, phiếu nhập `PN`, batch `{WH}-{SKU}-{ddMMyyyy}`, tem `TEM-000000`, giữ hàng, phiếu điều chuyển `DC` (bổ sung / **luân chuyển cận hạn**), **RotationPlanner + RotationJob** (CR-01), phiếu hủy `PH`, date tracker |
| Ordering | ordering | Đơn `DH-000000`, đồng bộ đơn offline (idempotency `clientRequestId`, xử lý theo thứ tự tới, tính lại khuyến mãi), chặn trái vùng, **AllocationEngine có trọng số**, picking list `DH-…A/B`, điều phối giao (snapshot xe + người giao, chi phí giao ước tính – CR-03), retailer quét QR nhận hàng, khiếu nại `KN` |
| Pricing | pricing | Khuyến mãi `KM-000` theo SKU / nhóm, giảm giá theo % HSD còn lại `KMB`, báo giá dùng chung cho server và app offline |
| Finance | finance | **Công nợ một phần**: khoản phải thu theo đơn công nợ (kỳ hạn 7/14/30, hạn = ngày giao + kỳ hạn), Chưa thu / Đã thu đủ / Quá hạn, thu một lần, job quá hạn + thông báo |
| Loyalty | loyalty | Chương trình `LP-000` (1 điểm / 100.000đ sau khuyến mãi), sổ điểm (Chờ cộng → Đã cộng, tạm giữ khi đổi, hoàn khi từ chối), quà `QUA-000`, yêu cầu đổi quà `DQ-yyMMdd-nn` |
| Performance | performance | KPI Sales, xếp hạng theo doanh thu **sau khuyến mãi** (chỉ đơn Sales chốt), sản phẩm bán chạy, dashboard (read model từ event của Ordering) |
| Notifications | notifications | Thông báo trong app + push (Azure Notification Hubs) |

Integration event chính: `OrderApproved/Cancelled` → Inventory giữ / trả hàng · `OrderDelivered` → Finance tạo khoản phải thu, Loyalty cộng điểm (Chờ cộng nếu công nợ) · `CreditOrderPaid` → Loyalty chuyển Đã cộng · `OrderCompleted` → Performance · `RotationProposalsCreated`, `ReceivableOverdue`, `GiftRedemption*` → Notifications.

## 3. Hai thuật toán lõi

Cả hai là hàm thuần trong `Domain`, cùng khung **hard filter → scoring có trọng số → greedy**, có trace để Portal hiển thị phần giải thích.

- `Ordering.Domain/Allocation/AllocationEngine` – phân bổ đơn: B1–B2 lọc cứng (hết hạn, hết hàng, > 20 km, ETA trễ, không đạt MLOR), B3 `S = 0,5·f_E + 0,3·f_D + 0,2·f_Q`, B4 greedy.
- `Inventory.Domain/Rotation/RotationPlanner` – luân chuyển cận hạn (CR-01): sellable window, excess, receiving capacity, lọc kho đích (≤ 40 km, v > 0, N ≥ 10, salvage value ≥ trip cost), `S = 0,5·sell-through safety + 0,3·distance + 0,2·excess coverage`, greedy, làm tròn bội 10.

Xem trace hai ví dụ trong tài liệu (DH-001030 → 40 TA + 20 TCH; WH-GV-SKU002-12102026 → 30 thùng sang Thới An, S = 0,667):

```bash
dotnet run --project tools/FSCM.AlgoDemo
```

## 4. Chạy local

Yêu cầu: .NET 8 SDK, Docker (hoặc PostgreSQL 16 cài sẵn).

```bash
docker compose up -d                       # PostgreSQL fscm/fscm trên localhost:5432
dotnet restore
dotnet run --project src/Hosts/FSCM.Api     # http://localhost:5080/swagger
dotnet run --project src/Hosts/FSCM.Worker  # cửa sổ khác: outbox + job hằng ngày
dotnet test
```

Ở môi trường `Development` (`appsettings.Development.json`):
- `Database:AutoCreate = true`: tự tạo schema của mọi module và seed tài khoản **admin** + bảng vận chuyển 09/10/2026.
- `Auth:Mode = Dev`: không cần Entra ID, gửi header `X-Dev-User: admin` (Swagger có nút Authorize cho header này). Tạo thêm user Operator / Sales / Warehouse Keeper qua `POST /api/v1/users`, rồi dùng id của họ trong header.

Production (Azure App Service + Azure Database for PostgreSQL): `Auth:Mode = Entra`, điền `AzureAd`, `ConnectionStrings:Fscm`, tắt `Database:AutoCreate`, dùng migration.

## 5. Migration (mỗi module một DbContext, một schema)

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add Init --project src/Modules/Inventory/FSCM.Modules.Inventory.Infrastructure --startup-project src/Hosts/FSCM.Api --context InventoryDbContext -o Migrations
dotnet ef database update --project src/Modules/Inventory/FSCM.Modules.Inventory.Infrastructure --startup-project src/Hosts/FSCM.Api --context InventoryDbContext
```
Lặp lại cho từng module (`IdentityDbContext`, `CatalogDbContext`, …) và `SharedDbContext` (project `FSCM.BuildingBlocks.Infrastructure`).

## 6. Quy ước

- Lỗi nghiệp vụ: `throw new BusinessRuleException("stock.insufficient", "Thông báo tiếng Việt")` hoặc `Guard.Against(...)` → API trả ProblemDetails 422 với `code`; mã kết thúc `not_found` → 404; `concurrency` (optimistic locking) → 409.
- Mã nghiệp vụ chỉ sinh qua `IdGenerator` / `IdFormat` (bảng `shared.id_sequence`), không tự ghép chuỗi trong service.
- Đổi trạng thái tồn kho chỉ qua aggregate có `Version` (optimistic locking); hai đơn tranh cùng batch → đơn sau nhận 409 và được phân bổ lại.
- Job hằng ngày implement `IScheduledJob` (giờ Asia/Ho_Chi_Minh). Local chạy bằng `DailyJobScheduler` trong Worker; trên Azure có thể gọi cùng class từ Azure Functions Timer Trigger.
- Thêm module: tạo 5 project theo mẫu, thêm `Infrastructure` + `Api` vào `FSCM.Api.csproj` và một dòng vào `ModuleCatalog` (và `WorkerModules`).
