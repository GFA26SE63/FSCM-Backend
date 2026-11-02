using System.Text.Json;
using FSCM.Application.Features.Identity;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Retailers;

public sealed class RetailerService(IRetailerStore store, TimeProvider timeProvider)
{
    private static readonly HashSet<int> AllowedCreditTerms = [0, 7, 14, 30];

    public async Task<IReadOnlyCollection<RetailerDetails>> ListAsync(
        Guid actorUserId, string actorRole, string? status, CancellationToken cancellationToken)
    {
        status = NormalizeStatusFilter(status);
        var views = await store.ListAsync(actorUserId, actorRole, status, cancellationToken);
        var mlors = await ListMlorLookupAsync(views.Select(item => item.Retailer.Id), false, cancellationToken);
        return views.Select(item => ToDetails(item, mlors.GetValueOrDefault(item.Retailer.Id, []))).ToArray();
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
        var view = visible.SingleOrDefault(item => item.Retailer.Id == retailerId)
            ?? throw RetailerOperationException.NotFound("Retailer was not found.");
        var mlors = await store.ListMlorDeclarationsAsync([retailerId], false, false, cancellationToken);
        return ToDetails(view, mlors);
    }

    public async Task<RetailerDetails> DeclareAsync(
        Guid actorUserId, Guid regionId, Guid salesGroupId, string name, string contactName,
        string phone, string email, string? address, string retailerType,
        decimal forecastRevenue, decimal storeSizeSquareMeters, decimal purchasingCapacity,
        int purchaseFrequencyPerMonth, int requestedCreditTermDays, string? assessmentNote,
        decimal? latitude, decimal? longitude, string? providerPlaceId,
        IReadOnlyCollection<RetailerMlorData>? mlorDeclarations, bool submitForReview,
        CancellationToken cancellationToken)
    {
        if (!await store.CanDeclareForGroupAsync(actorUserId, regionId, salesGroupId, cancellationToken))
            throw RetailerOperationException.Forbidden(
                "Sales can only declare a Retailer in their active Sales group's territory.");

        var input = Validate(name, contactName, phone, email, address, retailerType, forecastRevenue,
            storeSizeSquareMeters, purchasingCapacity, purchaseFrequencyPerMonth, requestedCreditTermDays, assessmentNote);
        await EnsureNoDuplicateAsync(input, null, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var retailer = new Retailer
        {
            Id = Guid.NewGuid(),
            RegionId = regionId,
            Code = await store.NextDeclarationCodeAsync(cancellationToken),
            Status = RetailerStatuses.Draft,
            CreditTermDays = 0,
            DeclaredByUserId = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };
        Apply(retailer, input, actorUserId, now);
        store.Add(retailer);
        store.AddDeclarationContact(new RetailerDeclarationContact
        {
            RetailerId = retailer.Id,
            ContactName = input.ContactName,
            Phone = input.Phone,
            Email = input.Email,
            CreatedAt = now,
            UpdatedAt = now
        });
        store.AddAssignment(new RetailerSalesAssignment
        {
            Id = Guid.NewGuid(),
            RetailerId = retailer.Id,
            SalesGroupId = salesGroupId,
            AssignedAt = now,
            IsActive = true
        });
        await ApplyLocationAsync(retailer.Id, latitude, longitude, providerPlaceId, now, cancellationToken);
        var activeMlors = mlorDeclarations is null
            ? (await ActiveMlorAsync(retailer.Id, cancellationToken)).Select(item => item.Mlor).ToArray()
            : await ReplaceMlorDeclarationsAsync(retailer, actorUserId, mlorDeclarations, now, cancellationToken);
        if (submitForReview)
        {
            EnsureMlorReadyForReview(activeMlors);
            retailer.Status = RetailerStatuses.PendingApproval;
        }
        AddAudit(retailer.Id, actorUserId, "RetailerDeclarationSaved", null,
            GeneralInformationAuditJson(retailer, input.ContactName, input.Phone, input.Email), null, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetResultAsync(retailer.Id, cancellationToken);
    }

    public async Task<RetailerDetails> UpdateDeclarationAsync(
        Guid retailerId, Guid actorUserId, string name, string contactName, string phone,
        string email, string? address, string retailerType, decimal forecastRevenue,
        decimal storeSizeSquareMeters, decimal purchasingCapacity, int purchaseFrequencyPerMonth,
        int requestedCreditTermDays, string? assessmentNote,
        decimal? latitude, decimal? longitude, string? providerPlaceId,
        IReadOnlyCollection<RetailerMlorData>? mlorDeclarations, bool submitForReview,
        CancellationToken cancellationToken)
    {
        var retailer = await store.FindForUpdateAsync(retailerId, cancellationToken)
            ?? throw RetailerOperationException.NotFound("Retailer was not found.");
        if (retailer.DeclaredByUserId != actorUserId)
            throw RetailerOperationException.Forbidden("Only the declaring Sales user can update this declaration.");
        if (retailer.Status is not (RetailerStatuses.Draft or RetailerStatuses.Rejected))
            throw RetailerOperationException.InvalidTransition("Only draft or rejected declarations can be updated.");

        var now = timeProvider.GetUtcNow();
        var contact = await store.FindDeclarationContactForUpdateAsync(retailerId, cancellationToken)
            ?? throw RetailerOperationException.InvalidTransition("Retailer declaration contact was not found.");
        var oldValues = GeneralInformationAuditJson(retailer, contact.ContactName, contact.Phone, contact.Email);
        var input = Validate(name, contactName, phone, email, address, retailerType, forecastRevenue,
            storeSizeSquareMeters, purchasingCapacity, purchaseFrequencyPerMonth, requestedCreditTermDays, assessmentNote);
        await EnsureNoDuplicateAsync(input, retailerId, cancellationToken);
        contact.ContactName = input.ContactName;
        contact.Phone = input.Phone;
        contact.Email = input.Email;
        contact.UpdatedAt = now;
        Apply(retailer, input, actorUserId, now);
        await ApplyLocationAsync(retailer.Id, latitude, longitude, providerPlaceId, now, cancellationToken);
        var activeMlors = mlorDeclarations is null
            ? (await ActiveMlorAsync(retailer.Id, cancellationToken)).Select(item => item.Mlor).ToArray()
            : await ReplaceMlorDeclarationsAsync(retailer, actorUserId, mlorDeclarations, now, cancellationToken);
        retailer.Status = RetailerStatuses.Draft;
        if (submitForReview)
        {
            EnsureMlorReadyForReview(activeMlors);
            retailer.Status = RetailerStatuses.PendingApproval;
        }
        retailer.ReviewedByUserId = null;
        retailer.ReviewedAt = null;
        retailer.ReviewReason = null;
        AddAudit(retailer.Id, actorUserId, "RetailerDeclarationUpdated", oldValues,
            GeneralInformationAuditJson(retailer, contact.ContactName, contact.Phone, contact.Email), null, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetResultAsync(retailerId, cancellationToken);
    }

    public async Task<RetailerDetails> SubmitForReviewAsync(
        Guid retailerId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var retailer = await store.FindForUpdateAsync(retailerId, cancellationToken)
            ?? throw RetailerOperationException.NotFound("Retailer was not found.");
        if (retailer.DeclaredByUserId != actorUserId)
            throw RetailerOperationException.Forbidden("Only the declaring Sales user can submit this declaration.");
        if (retailer.Status is not (RetailerStatuses.Draft or RetailerStatuses.Rejected))
            throw RetailerOperationException.InvalidTransition("Only a draft or rejected declaration can be submitted.");
        EnsureMlorReadyForReview(await ActiveMlorAsync(retailerId, cancellationToken));
        retailer.Status = RetailerStatuses.PendingApproval;
        retailer.ReviewedByUserId = null;
        retailer.ReviewedAt = null;
        retailer.ReviewReason = null;
        retailer.UpdatedAt = timeProvider.GetUtcNow();
        AddAudit(retailer.Id, actorUserId, "RetailerDeclarationSubmitted", null,
            JsonSerializer.Serialize(new { retailer.Status }), null, retailer.UpdatedAt);
        await store.SaveChangesAsync(cancellationToken);
        return await GetResultAsync(retailerId, cancellationToken);
    }

    public async Task<RetailerDetails> ReviewAsync(
        Guid retailerId, Guid reviewerUserId, bool approve, int? creditTermDays,
        string? reason, CancellationToken cancellationToken)
    {
        var retailer = await store.FindForUpdateAsync(retailerId, cancellationToken)
            ?? throw RetailerOperationException.NotFound("Retailer was not found.");
        if (retailer.Status != RetailerStatuses.PendingApproval)
            throw RetailerOperationException.InvalidTransition("Only a pending declaration can be reviewed.");
        EnsureMlorReadyForReview(await ActiveMlorAsync(retailerId, cancellationToken));
        reason = Clean(reason, 2000, "Review reason");
        if (string.IsNullOrWhiteSpace(reason))
            throw RetailerOperationException.Validation("A review reason is required for approval or rejection.");
        if (approve && (!creditTermDays.HasValue || !AllowedCreditTerms.Contains(creditTermDays.Value)))
            throw RetailerOperationException.Validation("Admin must select a payment term of 0, 7, 14, or 30 days when approving.");

        var reviewedAt = timeProvider.GetUtcNow();
        retailer.Status = approve ? RetailerStatuses.Active : RetailerStatuses.Rejected;
        if (approve)
        {
            if (!retailer.UserAccountId.HasValue)
            {
                var contact = await store.FindDeclarationContactForUpdateAsync(retailerId, cancellationToken)
                    ?? throw RetailerOperationException.InvalidTransition("Retailer declaration contact was not found.");
                var account = await store.FindUnassignedRetailerAccountAsync(
                    contact.Email.ToLowerInvariant(), contact.Phone, cancellationToken);
                if (account is null)
                {
                    account = new UserAccount
                    {
                        Id = Guid.NewGuid(),
                        IdentityProvider = IdentityProviders.Local,
                        Email = contact.Email,
                        Phone = contact.Phone,
                        FullName = contact.ContactName,
                        Role = SystemRoles.Retailer,
                        IsActive = false,
                        CreatedAt = reviewedAt,
                        UpdatedAt = reviewedAt
                    };
                    store.AddContactAccount(account);
                }
                else
                {
                    account.Email = contact.Email;
                    account.Phone = contact.Phone;
                    account.FullName = contact.ContactName;
                    account.UpdatedAt = reviewedAt;
                }
                retailer.UserAccountId = account.Id;
                store.RemoveDeclarationContact(contact);
            }
            retailer.Code = await store.NextBusinessCodeAsync(cancellationToken);
            retailer.CreditTermDays = creditTermDays!.Value;
        }
        retailer.ReviewedByUserId = reviewerUserId;
        retailer.ReviewedAt = reviewedAt;
        retailer.ReviewReason = reason;
        retailer.UpdatedAt = retailer.ReviewedAt.Value;
        AddAudit(retailer.Id, reviewerUserId, approve ? "RetailerDeclarationApproved" : "RetailerDeclarationRejected",
            null, JsonSerializer.Serialize(new { retailer.Status, retailer.CreditTermDays }), reason, retailer.UpdatedAt);
        await store.SaveChangesAsync(cancellationToken);
        return await GetResultAsync(retailerId, cancellationToken);
    }

    public async Task<RetailerDetails> SetActiveAsync(
        Guid retailerId, bool isActive, Guid actorUserId, CancellationToken cancellationToken)
    {
        var retailer = await store.FindForUpdateAsync(retailerId, cancellationToken)
            ?? throw RetailerOperationException.NotFound("Retailer was not found.");
        var requiredStatus = isActive ? RetailerStatuses.Inactive : RetailerStatuses.Active;
        if (retailer.Status != requiredStatus)
            throw RetailerOperationException.InvalidTransition(
                isActive ? "Only an inactive Retailer can be reactivated." : "Only an active Retailer can be inactivated.");
        if (isActive) EnsureMlorReadyForReview(await ActiveMlorAsync(retailerId, cancellationToken));
        retailer.Status = isActive ? RetailerStatuses.Active : RetailerStatuses.Inactive;
        retailer.UpdatedAt = timeProvider.GetUtcNow();
        AddAudit(retailer.Id, actorUserId,
            isActive ? "RetailerReactivated" : "RetailerInactivated", null,
            JsonSerializer.Serialize(new { retailer.Status }), null, retailer.UpdatedAt);
        await store.SaveChangesAsync(cancellationToken);
        return await GetResultAsync(retailerId, cancellationToken);
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

    public async Task<RetailerMlorResolutionResult> ResolveMlorAsync(
        Guid retailerId, Guid productId, Guid actorUserId, string actorRole,
        CancellationToken cancellationToken)
    {
        await GetAsync(retailerId, actorUserId, actorRole, cancellationToken);
        var product = await store.FindProductAsync(productId, cancellationToken)
            ?? throw RetailerOperationException.NotFound("Product was not found.");
        var active = await ActiveMlorAsync(retailerId, cancellationToken);
        var skuRule = active.SingleOrDefault(item => item.Mlor.ProductId == productId);
        if (skuRule is not null)
            return ToResolution(retailerId, productId, "Sku", skuRule, null);

        var categories = (await store.ListCategoriesAsync(cancellationToken)).ToDictionary(item => item.Id);
        var currentCategoryId = product.ProductCategoryId;
        var visited = new HashSet<Guid>();
        while (visited.Add(currentCategoryId))
        {
            var categoryRule = active.SingleOrDefault(item => item.Mlor.ProductCategoryId == currentCategoryId);
            if (categoryRule is not null)
                return ToResolution(retailerId, productId, "Category", categoryRule, currentCategoryId);
            if (!categories.TryGetValue(currentCategoryId, out var category) || !category.ParentCategoryId.HasValue)
                break;
            currentCategoryId = category.ParentCategoryId.Value;
        }

        var defaultConfiguration = await store.FindCurrentConfigurationAsync(
            BusinessConfigurationKeys.DefaultMlorRatio, cancellationToken)
            ?? throw RetailerOperationException.InvalidTransition("No default MLOR configuration is active.");
        using var document = JsonDocument.Parse(defaultConfiguration.ConfigValue);
        if (!document.RootElement.TryGetDecimal(out var defaultRatio) || defaultRatio <= 0 || defaultRatio >= 1)
            throw RetailerOperationException.InvalidTransition("The default MLOR configuration is invalid.");
        return new RetailerMlorResolutionResult(retailerId, productId, "Default", null,
            defaultConfiguration.Version, null, defaultRatio, null, timeProvider.GetUtcNow());
    }

    private async Task<RetailerView> RequireViewAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindViewAsync(id, cancellationToken)
        ?? throw RetailerOperationException.NotFound("Retailer was not found.");

    private async Task EnsureNoDuplicateAsync(
        RetailerInput input, Guid? excludedRetailerId, CancellationToken cancellationToken)
    {
        var duplicate = await store.FindDuplicateAsync(
            input.Name.ToLowerInvariant(),
            input.Phone.ToLowerInvariant(),
            input.Email.ToLowerInvariant(),
            input.Address?.ToLowerInvariant(),
            excludedRetailerId,
            cancellationToken);
        if (duplicate is not null)
        {
            throw RetailerOperationException.Conflict(
                $"Retailer declaration duplicates existing record '{duplicate.Code}'.");
        }
        if (await store.ContactAccountExistsAsync(input.Phone, input.Email, excludedRetailerId, cancellationToken))
            throw RetailerOperationException.Conflict("Phone or email is already assigned to another user account.");
    }

    private static RetailerInput Validate(
        string name, string? contactName, string? phone, string? email, string? address,
        string retailerType, decimal forecastRevenue, decimal storeSizeSquareMeters,
        decimal purchasingCapacity, int purchaseFrequencyPerMonth, int creditTermDays,
        string? assessmentNote)
    {
        name = Clean(name, 200, "Retailer name")!;
        if (name.Length < 2) throw RetailerOperationException.Validation("Retailer name must contain at least 2 characters.");
        contactName = Clean(contactName, 150, "Contact name")
            ?? throw RetailerOperationException.Validation("Contact name is required.");
        phone = Clean(phone, 20, "Phone");
        phone = phone is null ? null : new string(phone.Where(char.IsAsciiDigit).ToArray());
        if (phone is null || phone.Length != 10 || phone[0] != '0')
            throw RetailerOperationException.Validation("Phone must contain 10 digits and start with 0.");
        email = Clean(email, 150, "Email")?.ToLowerInvariant()
            ?? throw RetailerOperationException.Validation("Email is required.");
        try { _ = new System.Net.Mail.MailAddress(email); }
        catch (FormatException) { throw RetailerOperationException.Validation("Email is invalid."); }
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
        var valid = new[] { RetailerStatuses.Draft, RetailerStatuses.PendingApproval, RetailerStatuses.Active, RetailerStatuses.Rejected, RetailerStatuses.Inactive };
        return valid.FirstOrDefault(item => item.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw RetailerOperationException.Validation("Retailer status is invalid.");
    }

    private static string? Clean(string? value, int maxLength, string label)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value?.Length > maxLength) throw RetailerOperationException.Validation($"{label} cannot exceed {maxLength} characters.");
        return value;
    }

    private static RetailerDetails ToDetails(RetailerView view, IReadOnlyCollection<RetailerMlorView> mlors)
    {
        var item = view.Retailer;
        var generalInformation = new RetailerGeneralInformation(item.Name, view.ContactName, view.Phone,
            view.Email, item.Address, view.Location?.Latitude, view.Location?.Longitude,
            view.Location?.ProviderPlaceId, view.Location?.Version ?? 0, item.RetailerType,
            item.ForecastRevenue ?? 0, item.StoreSizeSquareMeters ?? 0, item.StoreSizeCategory,
            item.PurchasingCapacity ?? 0, item.PurchaseFrequencyPerMonth, item.RequestedCreditTermDays,
            item.PotentialStatus ?? "C", item.AssessmentNote);
        var declarations = mlors.Select(ToMlorResult).OrderBy(value => value.Scope)
            .ThenBy(value => value.CategoryCode ?? value.ProductSku, StringComparer.Ordinal).ToArray();
        return new(item.Id, item.RegionId, view.SalesGroupId, view.SalesGroupCode, item.Code, item.Name,
            view.ContactName, view.Phone, view.Email, item.Address,
            view.Location?.Latitude, view.Location?.Longitude, view.Location?.ProviderPlaceId,
            view.Location?.Version ?? 0, item.Status, item.RetailerType,
            item.ForecastRevenue ?? 0, item.StoreSizeSquareMeters ?? 0, item.StoreSizeCategory,
            item.PurchasingCapacity ?? 0, item.PurchaseFrequencyPerMonth, item.CreditTermDays,
            item.RequestedCreditTermDays,
            item.PotentialStatus ?? "C", item.AssessmentNote, item.DeclaredByUserId,
            item.ReviewedByUserId, item.ReviewedAt, item.ReviewReason, item.CreatedAt, item.UpdatedAt,
            generalInformation, declarations, declarations.Length > 0);
    }

    private async Task<RetailerDetails> GetResultAsync(Guid retailerId, CancellationToken cancellationToken)
    {
        var view = await RequireViewAsync(retailerId, cancellationToken);
        var mlors = await store.ListMlorDeclarationsAsync([retailerId], false, false, cancellationToken);
        return ToDetails(view, mlors);
    }

    private async Task<Dictionary<Guid, IReadOnlyCollection<RetailerMlorView>>> ListMlorLookupAsync(
        IEnumerable<Guid> retailerIds, bool includeInactive, CancellationToken cancellationToken)
    {
        var ids = retailerIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await store.ListMlorDeclarationsAsync(ids, includeInactive, false, cancellationToken))
            .GroupBy(item => item.Mlor.RetailerId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<RetailerMlorView>)group.ToArray());
    }

    private Task<IReadOnlyCollection<RetailerMlorView>> ActiveMlorAsync(
        Guid retailerId, CancellationToken cancellationToken) =>
        store.ListMlorDeclarationsAsync([retailerId], false, false, cancellationToken);

    private async Task<IReadOnlyCollection<RetailerProductMlor>> ReplaceMlorDeclarationsAsync(
        Retailer retailer, Guid actorUserId, IReadOnlyCollection<RetailerMlorData> definitions,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        definitions ??= [];
        var normalized = new List<RetailerMlorData>(definitions.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var hasCategory = definition.ProductCategoryId.HasValue;
            var hasProduct = definition.ProductId.HasValue;
            if (hasCategory == hasProduct)
                throw RetailerOperationException.Validation("Each MLOR declaration must select exactly one Category or SKU scope.");
            if (definition.MinimumLifeRatio <= 0 || definition.MinimumLifeRatio >= 1)
                throw RetailerOperationException.Validation("MLOR ratio must be greater than 0 and less than 1.");

            if (hasCategory)
            {
                var category = await store.FindCategoryAsync(definition.ProductCategoryId!.Value, cancellationToken)
                    ?? throw RetailerOperationException.NotFound("MLOR Category was not found.");
                if (!category.IsActive)
                    throw RetailerOperationException.Validation("MLOR Category must be active.");
                if (definition.MinimumLifeHours.HasValue)
                    throw RetailerOperationException.Validation("Minimum life hours can only be set for an SKU scope.");
            }
            else
            {
                var product = await store.FindProductAsync(definition.ProductId!.Value, cancellationToken)
                    ?? throw RetailerOperationException.NotFound("MLOR SKU was not found.");
                if (product.Status == "Inactive")
                    throw RetailerOperationException.Validation("MLOR SKU must not be inactive.");
                if (definition.MinimumLifeHours is < 0 || definition.MinimumLifeHours > product.ShelfLifeHours)
                    throw RetailerOperationException.Validation("Minimum life hours must be within the SKU shelf life.");
            }

            var key = hasCategory ? $"category:{definition.ProductCategoryId}" : $"sku:{definition.ProductId}";
            if (!keys.Add(key))
                throw RetailerOperationException.Validation("A declaration cannot contain duplicate MLOR scopes.");
            normalized.Add(definition);
        }

        var history = await store.ListMlorDeclarationsAsync([retailer.Id], true, true, cancellationToken);
        var retainedIds = new HashSet<Guid>();
        var active = new List<RetailerProductMlor>(normalized.Count);
        foreach (var definition in normalized)
        {
            var scopedHistory = history.Where(item => SameScope(item.Mlor, definition)).Select(item => item.Mlor).ToArray();
            var current = scopedHistory.SingleOrDefault(item => item.IsActive);
            if (current is not null && current.MinimumLifeRatio == definition.MinimumLifeRatio &&
                current.MinimumLifeHours == definition.MinimumLifeHours)
            {
                retainedIds.Add(current.Id);
                active.Add(current);
                continue;
            }
            if (current is not null)
            {
                current.IsActive = false;
                current.EffectiveTo = now;
            }
            var next = new RetailerProductMlor
            {
                Id = Guid.NewGuid(),
                RetailerId = retailer.Id,
                ProductCategoryId = definition.ProductCategoryId,
                ProductId = definition.ProductId,
                MinimumLifeRatio = definition.MinimumLifeRatio,
                MinimumLifeHours = definition.MinimumLifeHours,
                Version = scopedHistory.Length == 0 ? 1 : scopedHistory.Max(item => item.Version) + 1,
                UpdatedByUserId = actorUserId,
                EffectiveFrom = now,
                IsActive = true
            };
            store.AddMlor(next);
            active.Add(next);
        }

        foreach (var obsolete in history.Select(item => item.Mlor)
            .Where(item => item.IsActive && !retainedIds.Contains(item.Id) &&
                !active.Any(candidate => candidate.Id == item.Id)))
        {
            obsolete.IsActive = false;
            obsolete.EffectiveTo = now;
        }

        AddAudit(retailer.Id, actorUserId, "RetailerMlorDeclarationsReplaced", null,
            JsonSerializer.Serialize(active.Select(item => new
            {
                item.Id,
                item.ProductCategoryId,
                item.ProductId,
                item.MinimumLifeRatio,
                item.MinimumLifeHours,
                item.Version
            })), null, now);
        return active;
    }

    private static bool SameScope(RetailerProductMlor item, RetailerMlorData definition) =>
        item.ProductCategoryId == definition.ProductCategoryId && item.ProductId == definition.ProductId;

    private static void EnsureMlorReadyForReview(IReadOnlyCollection<RetailerMlorView> mlors) =>
        EnsureMlorReadyForReview(mlors.Select(item => item.Mlor).ToArray());

    private static void EnsureMlorReadyForReview(IReadOnlyCollection<RetailerProductMlor> mlors)
    {
        if (mlors.Count == 0 || mlors.Any(item => !item.IsActive ||
            (item.ProductCategoryId.HasValue == item.ProductId.HasValue) ||
            item.MinimumLifeRatio <= 0 || item.MinimumLifeRatio >= 1))
            throw RetailerOperationException.Validation(
                "A Retailer declaration requires at least one valid Category or SKU MLOR entry before review or activation.");
    }

    private void AddAudit(Guid retailerId, Guid actorUserId, string action,
        string? oldValues, string? newValues, string? reason, DateTimeOffset createdAt) =>
        store.AddAuditLog(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = actorUserId,
            EntityName = nameof(Retailer),
            EntityId = retailerId,
            Action = action,
            OldValues = oldValues,
            NewValues = newValues,
            Reason = reason,
            CreatedAt = createdAt
        });

    private static string GeneralInformationAuditJson(
        Retailer retailer, string contactName, string phone, string email) => JsonSerializer.Serialize(new
    {
        retailer.Name,
        ContactName = contactName,
        Phone = phone,
        Email = email,
        retailer.Address,
        retailer.RetailerType,
        retailer.ForecastRevenue,
        retailer.StoreSizeSquareMeters,
        retailer.PurchasingCapacity,
        retailer.PurchaseFrequencyPerMonth,
        retailer.RequestedCreditTermDays,
        retailer.PotentialStatus,
        retailer.AssessmentNote,
        retailer.Status
    });

    private static RetailerMlorDeclarationResult ToMlorResult(RetailerMlorView view) => new(
        view.Mlor.Id, view.Mlor.RetailerId,
        view.Mlor.ProductId.HasValue ? "Sku" : "Category",
        view.Mlor.ProductCategoryId, view.CategoryCode, view.CategoryName,
        view.Mlor.ProductId, view.ProductSku, view.ProductName,
        view.Mlor.MinimumLifeRatio, view.Mlor.MinimumLifeHours, view.Mlor.Version,
        view.Mlor.UpdatedByUserId, view.Mlor.EffectiveFrom, view.Mlor.EffectiveTo,
        view.Mlor.IsActive);

    private RetailerMlorResolutionResult ToResolution(
        Guid retailerId, Guid productId, string scope, RetailerMlorView view, Guid? categoryId) => new(
        retailerId, productId, scope, view.Mlor.Id, view.Mlor.Version, categoryId,
        view.Mlor.MinimumLifeRatio, view.Mlor.MinimumLifeHours, timeProvider.GetUtcNow());

    private sealed record RetailerInput(
        string Name, string ContactName, string Phone, string Email, string? Address,
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
