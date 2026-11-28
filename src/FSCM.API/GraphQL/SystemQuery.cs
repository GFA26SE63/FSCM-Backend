using System.Security.Claims;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Application.Features.TransitTables;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Application.Features.EntityQueries;
using FSCM.Application.Features.Identity;
using FSCM.Application.Features.Notifications;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;
using HotChocolate.Authorization;

namespace FSCM.API.GraphQL;

public sealed class SystemQuery
{
    [Authorize(Policy = "operation:BusinessConfigurations.List")]
    public async Task<IReadOnlyCollection<ConfigurationResult>> BusinessConfigurations(
        [Service] WarehouseConfigurationService service, [Service] IHttpContextAccessor accessor, CancellationToken ct)
    {
        var all = await service.ListCurrentConfigurationsAsync(ct);
        var user = accessor.HttpContext!.User;
        return user.IsInRole(SystemRoles.Admin) ||
            user.HasClaim(FSCM.API.Security.PermissionAuthorizationHandler.GrantClaim, SystemScreenKeys.Config)
            ? all : all.Where(item => item.Key is BusinessConfigurationKeys.OrderStepCartons
                or BusinessConfigurationKeys.DemoFastScanEnabled).ToArray();
    }

    [Authorize(Policy = "operation:TransitTables.Current")]
    public Task<TransitTableResult> CurrentTransitTable(
        [Service] TransitTableService service, CancellationToken ct) => service.GetCurrentAsync(ct);

    [Authorize(Policy = "operation:TransitTables.History")]
    public Task<IReadOnlyCollection<TransitTableResult>> TransitTableHistory(
        [Service] TransitTableService service, CancellationToken ct) => service.ListHistoryAsync(ct);

    [Authorize(Policy = "operation:Eta.Schedule")]
    public Task<AllocationScheduleResult> DeliverySchedule(
        [Service] DeliveryEtaService service, CancellationToken ct) => service.GetScheduleAsync(null, ct);

    [Authorize]
    public async Task<EntityQuery> Entities(
        [Service] IdentityService identityService,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var current = await identityService.GetCurrentUserAsync(
            CurrentUser.Id(accessor),
            cancellationToken);

        return new EntityQuery(
            new EntityQueryAccess(current.Id, current.Role, current.Scope, current.Permissions));
    }

    [Authorize]
    [Authorize(Policy = "operation:RetailerReceivables.List")]
    public Task<RetailerReceivablePage> RetailerReceivables(
        RetailerReceivableFilter filter,
        [Service] RetailerReceivableService service,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken) =>
        service.ListAsync(CurrentUser.Id(accessor), filter, cancellationToken);

    [Authorize]
    [Authorize(Policy = "operation:RetailerReceivables.GetByOrder")]
    public Task<RetailerReceivableResult> RetailerReceivable(
        Guid orderId,
        [Service] RetailerReceivableService service,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken) =>
        service.GetByOrderAsync(CurrentUser.Id(accessor), orderId, cancellationToken);

    [Authorize]
    [Authorize(Policy = "operation:Payments.List")]
    public Task<IReadOnlyCollection<PaymentResult>> Receivables(
        string? status,
        [Service] PaymentService service,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken) =>
        service.ListAsync(CurrentUser.Id(accessor), CurrentUser.Role(accessor), status, cancellationToken);

    [Authorize]
    [Authorize(Policy = "operation:Reports.SalesKpis")]
    public Task<SalesKpiResult> SalesKpis(
        SalesKpiQuery query,
        [Service] ReportService service,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken) =>
        service.GetSalesKpiAsync(query, CurrentUser.Id(accessor), CurrentUser.Role(accessor), cancellationToken);

    [Authorize]
    [Authorize(Policy = "operation:Reports.BestSellingProducts")]
    public Task<ProductRankingResult> BestSellingProducts(
        ProductRankingQuery query,
        [Service] ReportService service,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken) =>
        service.GetProductRankingAsync(query, CurrentUser.Id(accessor), CurrentUser.Role(accessor), cancellationToken);

    [Authorize]
    public Task<NotificationPage> Notifications(
        NotificationFilter filter,
        [Service] NotificationService service,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken) =>
        service.ListAsync(CurrentUser.Id(accessor), filter, cancellationToken);

    [Authorize]
    public Task<NotificationBadgeResult> NotificationBadges(
        [Service] NotificationService service,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken) =>
        service.GetBadgesAsync(CurrentUser.Id(accessor), cancellationToken);
}

internal static class CurrentUser
{
    public static Guid Id(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User
            ?? throw IdentityOperationException.InvalidToken();
        var rawId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(rawId, out var id)
            ? id
            : throw IdentityOperationException.InvalidToken();
    }

    public static string Role(IHttpContextAccessor accessor) =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role)
        ?? throw IdentityOperationException.InvalidToken();
}
