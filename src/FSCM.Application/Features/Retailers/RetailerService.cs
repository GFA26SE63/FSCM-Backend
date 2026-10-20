using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Retailers;

public sealed class RetailerService(IRetailerStore store, TimeProvider timeProvider)
{
    private static readonly HashSet<int> AllowedCreditTerms = [0, 7, 14, 30];

    public async Task<IReadOnlyCollection<RetailerDetails>> ListAsync(
        Guid actorUserId, string actorRole, string? status, CancellationToken cancellationToken)
    {
        status = NormalizeStatusFilter(status);
        return (await store.ListAsync(actorUserId, actorRole, status, cancellationToken)).Select(ToDetails).ToArray();
    }

    public async Task<RetailerPage> SearchAsync(
        Guid actorUserId, string actorRole, string? status, string? search, string? scope,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw RetailerOperationException.Validation("Page must be at least 1 and page size must be between 1 and 100.");
        if (!string.IsNullOrWhiteSpace(scope) &&
            (!scope.Equals("mine", StringComparison.OrdinalIgnoreCase) || actorRole != "Sales"))
            throw RetailerOperationException.Validation("Scope 'mine' is only available to Sales users.");
        var items = await ListAsync(actorUserId, actorRole, status, cancellationToken);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search is not null)
            items = items.Where(item => item.Code.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (item.Address?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).ToArray();
        var ordered = items.OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
        return new RetailerPage(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
            page, pageSize, ordered.Length);
    }

    public async Task<RetailerDetails> GetAsync(
        Guid retailerId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var visible = await store.ListAsync(actorUserId, actorRole, null, cancellationToken);
        return ToDetails(visible.SingleOrDefault(item => item.Retailer.Id == retailerId)
            ?? throw RetailerOperationException.NotFound("Retailer was not found."));
    }

    public async Task<RetailerDetails> DeclareAsync(
        Guid actorUserId, Guid regionId, Guid salesGroupId, string name, string? contactName,
        string? phone, string? email, string? address, string retailerType,
        decimal forecastRevenue, decimal storeSizeSquareMeters, decimal purchasingCapacity,
        int purchaseFrequencyPerMonth, int requestedCreditTermDays, string? assessmentNote,
        decimal? latitude, decimal? longitude, string? providerPlaceId,
        CancellationToken cancellationToken)
    {
        if (!await store.CanDeclareForGroupAsync(actorUserId, regionId, salesGroupId, cancellationToken))
            throw RetailerOperationException.Forbidden(
                "Sales can only declare a Retailer in their active Sales group's territory.");

        var input = Validate(name, contactName, phone, email, address, retailerType, forecastRevenue,
            storeSizeSquareMeters, purchasingCapacity, purchaseFrequencyPerMonth, requestedCreditTermDays, assessmentNote);
        var now = timeProvider.GetUtcNow();
        var retailer = new Retailer
        {
            Id = Guid.NewGuid(),
            RegionId = regionId,
            Code = await store.NextDeclarationCodeAsync(cancellationToken),
            Status = RetailerStatuses.PendingApproval,
            CreditTermDays = 0,
            DeclaredByUserId = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };
        Apply(retailer, input, actorUserId, now);
        store.Add(retailer);
        store.AddAssignment(new RetailerSalesAssignment
        {
            Id = Guid.NewGuid(),
            RetailerId = retailer.Id,
            SalesGroupId = salesGroupId,
            AssignedAt = now,
            IsActive = true
        });
        await ApplyLocationAsync(retailer.Id, latitude, longitude, providerPlaceId, now, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return ToDetails(await RequireViewAsync(retailer.Id, cancellationToken));
    }

    public async Task<RetailerDetails> UpdateDeclarationAsync(
        Guid retailerId, Guid actorUserId, string name, string? contactName, string? phone,
        string? email, string? address, string retailerType, decimal forecastRevenue,
        decimal storeSizeSquareMeters, decimal purchasingCapacity, int purchaseFrequencyPerMonth,
        int requestedCreditTermDays, string? assessmentNote,
        decimal? latitude, decimal? longitude, string? providerPlaceId,
        CancellationToken cancellationToken)
    {
        var retailer = await store.FindForUpdateAsync(retailerId, cancellationToken)
            ?? throw RetailerOperationException.NotFound("Retailer was not found.");
        if (retailer.DeclaredByUserId != actorUserId)
            throw RetailerOperationException.Forbidden("Only the declaring Sales user can update this declaration.");
        if (retailer.Status is not (RetailerStatuses.PendingApproval or RetailerStatuses.Rejected))
            throw RetailerOperationException.InvalidTransition("Only pending or rejected declarations can be updated.");

        var now = timeProvider.GetUtcNow();
        var input = Validate(name, contactName, phone, email, address, retailerType, forecastRevenue,
            storeSizeSquareMeters, purchasingCapacity, purchaseFrequencyPerMonth, requestedCreditTermDays, assessmentNote);
        Apply(retailer, input, actorUserId, now);
        await ApplyLocationAsync(retailer.Id, latitude, longitude, providerPlaceId, now, cancellationToken);
        retailer.Status = RetailerStatuses.PendingApproval;
        retailer.ReviewedByUserId = null;
        retailer.ReviewedAt = null;
        retailer.ReviewReason = null;
        await store.SaveChangesAsync(cancellationToken);
        return ToDetails(await RequireViewAsync(retailerId, cancellationToken));
    }

    public async Task<RetailerDetails> ReviewAsync(
        Guid retailerId, Guid reviewerUserId, bool approve, int? creditTermDays,
        string? reason, CancellationToken cancellationToken)
    {
        var retailer = await store.FindForUpdateAsync(retailerId, cancellationToken)
            ?? throw RetailerOperationException.NotFound("Retailer was not found.");
        if (retailer.Status != RetailerStatuses.PendingApproval)
            throw RetailerOperationException.InvalidTransition("Only a pending declaration can be reviewed.");
        reason = Clean(reason, 2000, "Review reason");
        if (!approve && string.IsNullOrWhiteSpace(reason))
            throw RetailerOperationException.Validation("A rejection reason is required.");
        if (approve && (!creditTermDays.HasValue || !AllowedCreditTerms.Contains(creditTermDays.Value)))
            throw RetailerOperationException.Validation("Admin must select a payment term of 0, 7, 14, or 30 days when approving.");

        retailer.Status = approve ? RetailerStatuses.Active : RetailerStatuses.Rejected;
        if (approve)
        {
            retailer.Code = await store.NextBusinessCodeAsync(cancellationToken);
            retailer.CreditTermDays = creditTermDays!.Value;
        }
        retailer.ReviewedByUserId = reviewerUserId;
        retailer.ReviewedAt = timeProvider.GetUtcNow();
        retailer.ReviewReason = reason;
        retailer.UpdatedAt = retailer.ReviewedAt.Value;
        await store.SaveChangesAsync(cancellationToken);
        return ToDetails(await RequireViewAsync(retailerId, cancellationToken));
    }

    public async Task<RetailerDetails> SetActiveAsync(
        Guid retailerId, bool isActive, CancellationToken cancellationToken)
    {
        var retailer = await store.FindForUpdateAsync(retailerId, cancellationToken)
            ?? throw RetailerOperationException.NotFound("Retailer was not found.");
        var requiredStatus = isActive ? RetailerStatuses.Inactive : RetailerStatuses.Active;
        if (retailer.Status != requiredStatus)
            throw RetailerOperationException.InvalidTransition(
                isActive ? "Only an inactive Retailer can be reactivated." : "Only an active Retailer can be inactivated.");
        retailer.Status = isActive ? RetailerStatuses.Active : RetailerStatuses.Inactive;
        retailer.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
        return ToDetails(await RequireViewAsync(retailerId, cancellationToken));
    }

    public async Task<RetailerOrderEligibility> CheckOrderEligibilityAsync(
        Guid salesUserId, Guid retailerId, CancellationToken cancellationToken)
    {
        var retailer = await store.FindViewAsync(retailerId, cancellationToken)
            ?? throw RetailerOperationException.NotFound("Retailer was not found.");
        var reasons = new List<string>();
        if (!await store.IsActiveSalesUserAsync(salesUserId, cancellationToken))
            reasons.Add("Only an active Sales user can create a Retailer order.");
        if (retailer.Retailer.Status != RetailerStatuses.Active)
            reasons.Add("Retailer must be approved and active before ordering.");
        if (!AllowedCreditTerms.Contains(retailer.Retailer.CreditTermDays))
            reasons.Add("Retailer credit term is invalid.");

        var access = reasons.Count == 0
            ? await store.FindTerritoryAccessAsync(salesUserId, retailerId, cancellationToken)
            : null;
        if (reasons.Count == 0 && access is null)
            reasons.Add("Retailer is outside the Sales user's assigned territory.");

        return new RetailerOrderEligibility(salesUserId, retailerId, reasons.Count == 0,
            access?.SalesGroupId, access?.SalesGroupCode, retailer.Retailer.Status,
            retailer.Retailer.CreditTermDays, reasons);
    }

    private async Task<RetailerView> RequireViewAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindViewAsync(id, cancellationToken)
        ?? throw RetailerOperationException.NotFound("Retailer was not found.");

    private static RetailerInput Validate(
        string name, string? contactName, string? phone, string? email, string? address,
        string retailerType, decimal forecastRevenue, decimal storeSizeSquareMeters,
        decimal purchasingCapacity, int purchaseFrequencyPerMonth, int creditTermDays,
        string? assessmentNote)
    {
        name = Clean(name, 200, "Retailer name")!;
        if (name.Length < 2) throw RetailerOperationException.Validation("Retailer name must contain at least 2 characters.");
        contactName = Clean(contactName, 200, "Contact name");
        phone = Clean(phone, 30, "Phone");
        email = Clean(email, 150, "Email")?.ToLowerInvariant();
        address = Clean(address, 2000, "Address");
        assessmentNote = Clean(assessmentNote, 4000, "Assessment note");
        retailerType = retailerType.Trim();
        if (!RetailerTypes.All.Contains(retailerType))
            throw RetailerOperationException.Validation("Retailer type is invalid.");
        if (forecastRevenue < 0 || purchasingCapacity < 0 || storeSizeSquareMeters <= 0)
            throw RetailerOperationException.Validation("Revenue and capacity cannot be negative, and store size must be positive.");
        if (purchaseFrequencyPerMonth <= 0)
            throw RetailerOperationException.Validation("Purchase frequency must be greater than zero.");
        if (!AllowedCreditTerms.Contains(creditTermDays))
            throw RetailerOperationException.Validation("Credit term must be 0, 7, 14, or 30 days.");
        return new(name, contactName, phone, email, address, retailerType, forecastRevenue,
            storeSizeSquareMeters, purchasingCapacity, purchaseFrequencyPerMonth, creditTermDays, assessmentNote);
    }

    private static void Apply(Retailer retailer, RetailerInput input, Guid assessorId, DateTimeOffset now)
    {
        retailer.Name = input.Name;
        retailer.ContactName = input.ContactName;
        retailer.Phone = input.Phone;
        retailer.Email = input.Email;
        retailer.Address = input.Address;
        retailer.RetailerType = input.RetailerType;
        retailer.ForecastRevenue = input.ForecastRevenue;
        retailer.StoreSizeSquareMeters = input.StoreSizeSquareMeters;
        retailer.StoreSizeCategory = input.StoreSizeSquareMeters < 30 ? "S" : input.StoreSizeSquareMeters <= 60 ? "M" : "L";
        retailer.PurchasingCapacity = input.PurchasingCapacity;
        retailer.PurchaseFrequencyPerMonth = input.PurchaseFrequencyPerMonth;
        retailer.RequestedCreditTermDays = input.RequestedCreditTermDays;
        retailer.PotentialStatus = input.ForecastRevenue >= 60_000_000 || input.PurchasingCapacity >= 120 ? "A"
            : input.ForecastRevenue >= 30_000_000 || input.PurchasingCapacity >= 60 ? "B" : "C";
        retailer.PotentialScore = null;
        retailer.AssessmentNote = input.AssessmentNote;
        retailer.AssessedByUserId = assessorId;
        retailer.AssessedAt = now;
        retailer.UpdatedAt = now;
    }

    private static string? NormalizeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var valid = new[] { RetailerStatuses.PendingApproval, RetailerStatuses.Active, RetailerStatuses.Rejected, RetailerStatuses.Inactive };
        return valid.FirstOrDefault(item => item.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw RetailerOperationException.Validation("Retailer status is invalid.");
    }

    private static string? Clean(string? value, int maxLength, string label)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value?.Length > maxLength) throw RetailerOperationException.Validation($"{label} cannot exceed {maxLength} characters.");
        return value;
    }

    private static RetailerDetails ToDetails(RetailerView view)
    {
        var item = view.Retailer;
        return new(item.Id, item.RegionId, view.SalesGroupId, view.SalesGroupCode, item.Code, item.Name,
            item.ContactName, item.Phone, item.Email, item.Address,
            view.Location?.Latitude, view.Location?.Longitude, view.Location?.ProviderPlaceId,
            view.Location?.Version ?? 0, item.Status, item.RetailerType,
            item.ForecastRevenue ?? 0, item.StoreSizeSquareMeters ?? 0, item.StoreSizeCategory,
            item.PurchasingCapacity ?? 0, item.PurchaseFrequencyPerMonth, item.CreditTermDays,
            item.RequestedCreditTermDays,
            item.PotentialStatus ?? "C", item.AssessmentNote, item.DeclaredByUserId,
            item.ReviewedByUserId, item.ReviewedAt, item.ReviewReason, item.CreatedAt, item.UpdatedAt);
    }

    private sealed record RetailerInput(
        string Name, string? ContactName, string? Phone, string? Email, string? Address,
        string RetailerType, decimal ForecastRevenue, decimal StoreSizeSquareMeters,
        decimal PurchasingCapacity, int PurchaseFrequencyPerMonth, int RequestedCreditTermDays,
        string? AssessmentNote);

    private async Task ApplyLocationAsync(
        Guid retailerId, decimal? latitude, decimal? longitude, string? providerPlaceId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (latitude.HasValue != longitude.HasValue)
            throw RetailerOperationException.Validation("Latitude and longitude must be supplied together.");
        if (!latitude.HasValue) return;
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            throw RetailerOperationException.Validation("Retailer coordinates are outside the valid range.");
        var location = await store.FindLocationForUpdateAsync(retailerId, cancellationToken);
        if (location is null)
        {
            location = new MapLocation
            {
                Id = Guid.NewGuid(),
                RetailerId = retailerId,
                Provider = "Manual",
                Version = 1,
                CreatedAt = now
            };
            store.AddLocation(location);
        }
        else
        {
            location.Version++;
        }
        location.Latitude = latitude.Value;
        location.Longitude = longitude!.Value;
        location.ProviderPlaceId = string.IsNullOrWhiteSpace(providerPlaceId) ? null : providerPlaceId.Trim();
        location.NormalizedAt = null;
        location.UpdatedAt = now;
    }
}
