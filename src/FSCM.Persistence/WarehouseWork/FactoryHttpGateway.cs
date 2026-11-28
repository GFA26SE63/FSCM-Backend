using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using FSCM.Application.Features.Picking;
using FSCM.Domain.Entities;

namespace FSCM.Persistence.WarehouseWork;

public sealed record FactorySourceTrace(FactoryProductionBatch Batch, string Sku, FactoryPallet? Pallet, FactoryCarton? Carton,
    FactoryProductUnit? Unit, FactoryProductUnit[] Units, string[] CartonCodes, Guid[] DestinationWarehouseIds);

/// <summary>Dispatch note (PXN) a pallet left the factory on; null for pallets dispatched before dispatch notes existed.</summary>
public sealed record FactoryPalletDispatchInfo(Guid PalletId, Guid? DispatchNoteId, string? DispatchCode);
public interface IFactoryGateway
{
    Task<FactoryInboundPallet[]> DispatchedAsync(Guid[] warehouseIds, CancellationToken ct);
    Task<PalletManifest> ManifestAsync(string code, CancellationToken ct);
    Task<FactorySourceTrace> TraceAsync(string code, CancellationToken ct);
    Task<Guid> AcknowledgeReceiptAsync(FactoryReceiptSync receipt, CancellationToken ct);
    Task<FactoryPalletDispatchInfo[]> PalletDispatchInfoAsync(Guid[] palletIds, CancellationToken ct);
}

public sealed class FactoryHttpGateway(HttpClient http) : IFactoryGateway
{
    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw PickingOperationException.NotFound("Không tìm thấy mã trong hệ thống traceability.");
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw PickingOperationException.Conflict("Traceability từ chối thao tác do trạng thái dữ liệu đã thay đổi. Làm mới và thử lại.");
            if (!response.IsSuccessStatusCode)
                throw PickingOperationException.ServiceUnavailable("Dịch vụ nhà máy chưa sẵn sàng. Vui lòng thử lại sau hoặc liên hệ quản trị viên.");
            return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw PickingOperationException.BadGateway("Dữ liệu phản hồi từ nhà máy không hợp lệ.");
        }
        catch (HttpRequestException) { throw PickingOperationException.ServiceUnavailable("Không kết nối được dịch vụ nhà máy. Vui lòng thử lại sau."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw PickingOperationException.ServiceUnavailable("Dịch vụ nhà máy phản hồi quá thời gian chờ. Vui lòng thử lại sau."); }
        catch (JsonException) { throw PickingOperationException.BadGateway("Dữ liệu phản hồi từ nhà máy không hợp lệ."); }
    }
    public Task<PalletManifest> ManifestAsync(string code, CancellationToken ct) => Send<PalletManifest>(HttpMethod.Get, $"api/integration/pallets/{Uri.EscapeDataString(code)}/manifest", null, ct);
    public Task<FactoryInboundPallet[]> DispatchedAsync(Guid[] warehouseIds, CancellationToken ct) =>
        warehouseIds.Length == 0 ? Task.FromResult(Array.Empty<FactoryInboundPallet>()) :
        Send<FactoryInboundPallet[]>(HttpMethod.Get, "api/integration/dispatches?" +
            string.Join("&", warehouseIds.Distinct().Select(id => $"warehouseIds={id:D}")), null, ct);
    public Task<FactorySourceTrace> TraceAsync(string code, CancellationToken ct) => Send<FactorySourceTrace>(HttpMethod.Get, $"api/integration/trace?code={Uri.EscapeDataString(code)}", null, ct);
    public async Task<Guid> AcknowledgeReceiptAsync(FactoryReceiptSync receipt, CancellationToken ct)
    {
        var response = await Send<Acknowledgement>(HttpMethod.Post, "api/integration/receipts", new {
            receipt.PalletId, receipt.ReceiptDocumentId, receipt.WarehouseId, receipt.ReceivedAt, receipt.ReceiptInspectionJson
        }, ct);
        if (response.ReceiptInspectionJson != receipt.ReceiptInspectionJson)
            throw PickingOperationException.Conflict("Traceability chưa xác nhận chi tiết kiểm đếm; cần cập nhật dịch vụ nguồn.");
        return response.Id;
    }
    public Task<FactoryPalletDispatchInfo[]> PalletDispatchInfoAsync(Guid[] palletIds, CancellationToken ct) =>
        palletIds.Length == 0 ? Task.FromResult(Array.Empty<FactoryPalletDispatchInfo>()) :
        Send<FactoryPalletDispatchInfo[]>(HttpMethod.Get, "api/integration/pallets/dispatch-info?" +
            string.Join("&", palletIds.Distinct().Select(id => $"palletIds={id:D}")), null, ct);
    private sealed record Acknowledgement(Guid Id, string? ReceiptInspectionJson);
}
