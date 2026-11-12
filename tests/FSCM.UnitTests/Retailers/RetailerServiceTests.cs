using FSCM.Application.Features.Identity;
using FSCM.Application.Features.Retailers;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Entities;
using FSCM.UnitTests.Storage;

namespace FSCM.UnitTests.Retailers;

public sealed class RetailerServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Declaration_calculates_size_and_class_a_and_assigns_temporary_code()
    {
        var fixture = new Fixture();

        var result = await fixture.DeclareAsync(59_000_000, 29, 121);

        Assert.Equal("KB-0100", result.Code);
        Assert.Equal("S", result.StoreSizeCategory);
        Assert.Equal("A", result.PotentialClass);
        Assert.Equal(RetailerStatuses.PendingApproval, result.Status);
        Assert.Equal(0, result.CreditTermDays);
        Assert.Equal(7, result.RequestedCreditTermDays);
        Assert.Equal(10.776m, result.Latitude);
        Assert.Single(fixture.Store.Assignments);
        Assert.Empty(fixture.Store.ContactAccounts);
        Assert.Single(fixture.Store.DeclarationContacts);
        Assert.Equal(result.ContactName, fixture.Store.DeclarationContacts[0].ContactName);
        Assert.Null(fixture.Store.Retailers[0].UserAccountId);
    }

    [Fact]
    public async Task Approval_activates_retailer_and_replaces_declaration_code()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(30_000_000, 20, 60);

        var result = await fixture.Service.ReviewAsync(declared.Id, Guid.NewGuid(), true, 14, "Hồ sơ hợp lệ", CancellationToken.None);

        Assert.Equal("10005000", result.Code);
        Assert.Equal(RetailerStatuses.Active, result.Status);
        Assert.Equal(14, result.CreditTermDays);
        Assert.Equal("B", result.PotentialClass);
        Assert.Single(fixture.Store.ContactAccounts);
        Assert.Empty(fixture.Store.DeclarationContacts);
        Assert.Equal(fixture.Store.ContactAccounts[0].Id, fixture.Store.Retailers[0].UserAccountId);
    }

    [Fact]
    public async Task Approval_creates_inactive_retailer_account_and_sends_account_created_email()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(30_000_000, 20, 60);

        var result = await fixture.Service.ReviewAsync(declared.Id, Guid.NewGuid(), true, 7, "Hồ sơ hợp lệ", CancellationToken.None);

        var account = Assert.Single(fixture.Store.ContactAccounts);
        Assert.False(account.IsActive);
        Assert.Equal("Retailer", account.Role);
        var email = Assert.Single(fixture.EmailSender.AccountCreatedEmails);
        Assert.Equal("retailer.test@fscm.local", email.RecipientEmail);
        Assert.Equal("0909000000", email.Phone);
        Assert.Equal(result.Code, email.RetailerCode);
        Assert.Contains(fixture.Store.AuditLogs, log => log.Action == "RetailerAccountCreatedEmailSent");
    }

    [Fact]
    public async Task Approval_still_succeeds_when_account_created_email_fails()
    {
        var fixture = new Fixture();
        fixture.EmailSender.Fail = true;
        var declared = await fixture.DeclareAsync(30_000_000, 20, 60);

        var result = await fixture.Service.ReviewAsync(declared.Id, Guid.NewGuid(), true, 7, "Hồ sơ hợp lệ", CancellationToken.None);

        Assert.Equal(RetailerStatuses.Active, result.Status);
        Assert.Single(fixture.Store.ContactAccounts);
        Assert.Contains(fixture.Store.AuditLogs, log => log.Action == "RetailerAccountCreatedEmailFailed");
    }

    [Fact]
    public async Task Approval_reuses_single_unassigned_retailer_account()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(30_000_000, 20, 60);
        var existing = UnassignedAccount("retailer.test@fscm.local", "0911111111");
        fixture.Store.ContactAccounts.Add(existing);

        await fixture.Service.ReviewAsync(declared.Id, Guid.NewGuid(), true, 7, "Hồ sơ hợp lệ", CancellationToken.None);

        Assert.Single(fixture.Store.ContactAccounts);
        Assert.Equal(existing.Id, fixture.Store.Retailers[0].UserAccountId);
        Assert.Equal("0909000000", existing.Phone);
    }

    [Fact]
    public async Task Approval_with_multiple_matching_unassigned_accounts_returns_conflict()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(30_000_000, 20, 60);
        fixture.Store.ContactAccounts.Add(UnassignedAccount("retailer.test@fscm.local", "0911111111"));
        fixture.Store.ContactAccounts.Add(UnassignedAccount("other@fscm.local", "0909000000"));

        var exception = await Assert.ThrowsAsync<RetailerOperationException>(() =>
            fixture.Service.ReviewAsync(declared.Id, Guid.NewGuid(), true, 7, "Hồ sơ hợp lệ", CancellationToken.None));

        Assert.Equal("conflict", exception.Code);
        Assert.Null(fixture.Store.Retailers[0].UserAccountId);
        Assert.Empty(fixture.EmailSender.AccountCreatedEmails);
    }

    private static UserAccount UnassignedAccount(string email, string phone) => new()
    {
        Id = Guid.NewGuid(), Email = email, Phone = phone, FullName = "Tài khoản có sẵn",
        Role = "Retailer", IsActive = false, IdentityProvider = "Local"
    };

    [Fact]
    public async Task Set_storefront_image_stores_key_and_exposes_url()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(30_000_000, 20, 60);

        var result = await fixture.Service.SetStorefrontImageAsync(
            declared.Id, fixture.SalesUserId, "Sales", FakeImageFactory.Png(), CancellationToken.None);

        Assert.NotNull(result.StorefrontImageUrl);
        Assert.NotNull(fixture.Store.Retailers[0].StorefrontImageBlobKey);
        Assert.Single(fixture.Images.Blobs);
    }

    [Fact]
    public async Task Retailer_cannot_set_storefront_image_of_another_account()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(30_000_000, 20, 60);

        var error = await Assert.ThrowsAsync<RetailerOperationException>(() =>
            fixture.Service.SetStorefrontImageAsync(
                declared.Id, Guid.NewGuid(), "Retailer", FakeImageFactory.Png(), CancellationToken.None));

        Assert.Equal("forbidden", error.Code);
    }

    [Fact]
    public async Task Rejection_does_not_send_account_created_email()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(10_000_000, 20, 20);

        await fixture.Service.ReviewAsync(declared.Id, Guid.NewGuid(), false, null, "Hồ sơ chưa hợp lệ", CancellationToken.None);

        Assert.Empty(fixture.EmailSender.AccountCreatedEmails);
    }

    [Fact]
    public async Task Search_scope_mine_returns_only_own_declarations()
    {
        var fixture = new Fixture();
        await fixture.DeclareAsync(30_000_000, 20, 60);

        var own = await fixture.Service.SearchAsync(fixture.SalesUserId, "Sales", null, null, "mine", 1, 20, CancellationToken.None);
        var other = await fixture.Service.SearchAsync(Guid.NewGuid(), "Sales", null, null, "mine", 1, 20, CancellationToken.None);

        Assert.Equal(1, own.TotalCount);
        Assert.Equal(0, other.TotalCount);
    }

    [Fact]
    public async Task Rejection_requires_reason()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(10_000_000, 20, 20);

        var exception = await Assert.ThrowsAsync<RetailerOperationException>(() =>
            fixture.Service.ReviewAsync(declared.Id, Guid.NewGuid(), false, null, null, CancellationToken.None));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Rejection_does_not_create_or_assign_user_account()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(10_000_000, 20, 20);

        var result = await fixture.Service.ReviewAsync(
            declared.Id, Guid.NewGuid(), false, null, "Hồ sơ chưa hợp lệ", CancellationToken.None);

        Assert.Equal(RetailerStatuses.Rejected, result.Status);
        Assert.Empty(fixture.Store.ContactAccounts);
        Assert.Single(fixture.Store.DeclarationContacts);
        Assert.Null(fixture.Store.Retailers[0].UserAccountId);
    }

    [Fact]
    public async Task Approval_requires_reason()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(10_000_000, 20, 20);

        var exception = await Assert.ThrowsAsync<RetailerOperationException>(() =>
            fixture.Service.ReviewAsync(declared.Id, Guid.NewGuid(), true, 7, null, CancellationToken.None));

        Assert.Equal("validation_error", exception.Code);
        Assert.Equal(RetailerStatuses.PendingApproval, declared.Status);
    }

    [Fact]
    public async Task Duplicate_retailer_declaration_is_rejected()
    {
        var fixture = new Fixture();
        await fixture.DeclareAsync(10_000_000, 20, 20);

        var exception = await Assert.ThrowsAsync<RetailerOperationException>(() =>
            fixture.DeclareAsync(20_000_000, 30, 40));

        Assert.Equal("conflict", exception.Code);
        Assert.Single(fixture.Store.Retailers);
    }

    [Fact]
    public async Task Draft_cannot_be_submitted_without_a_valid_mlor_declaration()
    {
        var fixture = new Fixture();
        var draft = await fixture.DeclareAsync(10_000_000, 20, 20, includeMlor: false, submitForReview: false);

        var exception = await Assert.ThrowsAsync<RetailerOperationException>(() =>
            fixture.Service.SubmitForReviewAsync(draft.Id, fixture.SalesUserId, CancellationToken.None));

        Assert.Equal(RetailerStatuses.Draft, draft.Status);
        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Mlor_resolution_prefers_sku_then_most_specific_category()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(20_000_000, 20, 40);
        fixture.Store.Mlors.Add(new RetailerProductMlor
        {
            Id = Guid.NewGuid(),
            RetailerId = declared.Id,
            ProductCategoryId = fixture.ParentCategory.Id,
            MinimumLifeRatio = 0.2m,
            Version = 1,
            UpdatedByUserId = fixture.SalesUserId,
            EffectiveFrom = Now,
            IsActive = true
        });

        var categoryResult = await fixture.Service.ResolveMlorAsync(declared.Id, fixture.Product.Id,
            fixture.SalesUserId, "Sales", CancellationToken.None);

        Assert.Equal("Category", categoryResult.SourceScope);
        Assert.Equal(fixture.Category.Id, categoryResult.ProductCategoryId);
        Assert.Equal(0.35m, categoryResult.MinimumLifeRatio);

        fixture.Store.Mlors.Add(new RetailerProductMlor
        {
            Id = Guid.NewGuid(),
            RetailerId = declared.Id,
            ProductId = fixture.Product.Id,
            MinimumLifeRatio = 0.6m,
            Version = 2,
            UpdatedByUserId = fixture.SalesUserId,
            EffectiveFrom = Now,
            IsActive = true
        });

        var result = await fixture.Service.ResolveMlorAsync(declared.Id, fixture.Product.Id,
            fixture.SalesUserId, "Sales", CancellationToken.None);

        Assert.Equal("Sku", result.SourceScope);
        Assert.Equal(0.6m, result.MinimumLifeRatio);
        Assert.Equal(2, result.SourceVersion);
    }

    [Fact]
    public async Task Order_eligibility_requires_active_retailer_and_matching_territory()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(65_000_000, 8, 100);
        var pending = await fixture.Service.CheckOrderEligibilityAsync(fixture.SalesUserId, declared.Id, CancellationToken.None);
        await fixture.Service.ReviewAsync(declared.Id, Guid.NewGuid(), true, 7, "Hồ sơ hợp lệ", CancellationToken.None);
        fixture.Store.HasTerritoryAccess = true;

        var active = await fixture.Service.CheckOrderEligibilityAsync(fixture.SalesUserId, declared.Id, CancellationToken.None);

        Assert.False(pending.IsEligible);
        Assert.True(active.IsEligible);
    }

    private static UpdateRetailerProfileData ProfileData() => new(
        "Updated store", "Updated contact", "0909888777", "updated@retailer.test", "Updated address",
        RetailerTypes.GroceryStore, 40_000_000, 45, 80, 5, 30, "Updated assessment",
        10.8m, 106.7m, null);

    [Fact]
    public async Task Admin_profile_edit_updates_linked_contact_without_reopening_approval_or_changing_credit_and_mlor()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(30_000_000, 20, 60);
        var reviewer = Guid.NewGuid();
        await fixture.Service.ReviewAsync(declared.Id, reviewer, true, 14, "Approved", default);
        var retailer = Assert.Single(fixture.Store.Retailers);
        var account = Assert.Single(fixture.Store.ContactAccounts);
        var code = retailer.Code;
        var accountId = retailer.UserAccountId;
        var mlor = Assert.Single(fixture.Store.Mlors);
        var mlorVersion = mlor.Version;
        var region = retailer.RegionId;

        var result = await fixture.Service.UpdateProfileAsync(declared.Id, Guid.NewGuid(), "Admin", ProfileData(), default);

        Assert.Equal("Updated store", result.Name);
        Assert.Equal("Updated contact", result.ContactName);
        Assert.Equal("updated@retailer.test", account.Email);
        Assert.Equal("0909888777", account.Phone);
        Assert.Equal("Retailer", account.Role);
        Assert.False(account.IsActive); // Profile editing must not bypass account activation.
        Assert.Equal(accountId, retailer.UserAccountId);
        Assert.Equal(code, retailer.Code);
        Assert.Equal(region, retailer.RegionId);
        Assert.Equal(RetailerStatuses.Active, retailer.Status);
        Assert.Equal(reviewer, retailer.ReviewedByUserId);
        Assert.Equal("Approved", retailer.ReviewReason);
        Assert.Equal(14, retailer.CreditTermDays);
        Assert.Equal(fixture.SalesUserId, retailer.DeclaredByUserId);
        Assert.Single(fixture.Store.Assignments);
        Assert.Same(mlor, Assert.Single(fixture.Store.Mlors));
        Assert.Equal(mlorVersion, mlor.Version);
        Assert.True(mlor.IsActive);
        Assert.Equal(10.8m, result.Latitude);
        Assert.Single(fixture.EmailSender.AccountCreatedEmails);
        Assert.Contains(fixture.Store.AuditLogs, log => log.Action == "RetailerProfileUpdated");
    }

    [Fact]
    public async Task Admin_profile_edit_updates_declaration_contact_and_keeps_pending_review()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(30_000_000, 20, 60);
        var result = await fixture.Service.UpdateProfileAsync(declared.Id, Guid.NewGuid(), "Admin", ProfileData(), default);
        Assert.Equal(RetailerStatuses.PendingApproval, result.Status);
        Assert.Equal("updated@retailer.test", Assert.Single(fixture.Store.DeclarationContacts).Email);
        Assert.Empty(fixture.Store.ContactAccounts);
        Assert.Null(Assert.Single(fixture.Store.Retailers).UserAccountId);
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Sales")]
    [InlineData("SalesManager")]
    [InlineData("Retailer")]
    [InlineData("WarehouseKeeper")]
    public async Task Profile_edit_is_admin_only_in_the_service_as_well_as_the_controller(string role)
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(30_000_000, 20, 60);
        var exception = await Assert.ThrowsAsync<RetailerOperationException>(() =>
            fixture.Service.UpdateProfileAsync(declared.Id, fixture.SalesUserId, role, ProfileData(), default));
        Assert.Equal("forbidden", exception.Code);
        Assert.Equal(declared.Name, Assert.Single(fixture.Store.Retailers).Name);
    }

    [Fact]
    public async Task Profile_edit_rejects_duplicate_contacts_and_invalid_coordinates_without_changing_profile()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(30_000_000, 20, 60);
        fixture.Store.ContactAccounts.Add(UnassignedAccount("updated@retailer.test", "0909888777"));
        await Assert.ThrowsAsync<RetailerOperationException>(() =>
            fixture.Service.UpdateProfileAsync(declared.Id, Guid.NewGuid(), "Admin", ProfileData(), default));
        fixture.Store.ContactAccounts.Clear();
        await Assert.ThrowsAsync<RetailerOperationException>(() =>
            fixture.Service.UpdateProfileAsync(declared.Id, Guid.NewGuid(), "Admin", ProfileData() with { Latitude = 91 }, default));
        await Assert.ThrowsAsync<RetailerOperationException>(() =>
            fixture.Service.UpdateProfileAsync(declared.Id, Guid.NewGuid(), "Admin", ProfileData() with { Name = " " }, default));
        Assert.Equal(declared.Name, Assert.Single(fixture.Store.Retailers).Name);
        Assert.Equal(declared.Email, Assert.Single(fixture.Store.DeclarationContacts).Email);
    }

    private sealed class Fixture
    {
        public Guid SalesUserId { get; } = Guid.NewGuid();
        public Guid RegionId { get; } = Guid.NewGuid();
        public Guid GroupId { get; } = Guid.NewGuid();
        public FakeStore Store { get; } = new();
        public RetailerService Service { get; }
        public ProductCategory ParentCategory { get; }
        public ProductCategory Category { get; }
        public Product Product { get; }

        public Fixture()
        {
            ParentCategory = new ProductCategory { Id = Guid.NewGuid(), Code = "CAT-01", Name = "Đồ uống", IsActive = true };
            Category = new ProductCategory
            {
                Id = Guid.NewGuid(),
                ParentCategoryId = ParentCategory.Id,
                Code = "CAT-02",
                Name = "Sữa",
                IsActive = true
            };
            Product = new Product
            {
                Id = Guid.NewGuid(),
                ProductCategoryId = Category.Id,
                Sku = "SKU001",
                Name = "Sữa tươi",
                ShelfLifeHours = 168,
                Status = "Published"
            };
            Store.Categories.AddRange([ParentCategory, Category]);
            Store.Products.Add(Product);
            Store.Configurations.Add(new SystemConfiguration
            {
                ConfigKey = BusinessConfigurationKeys.DefaultMlorRatio,
                ConfigValue = "0.2",
                Version = 1,
                IsActive = true
            });
            Service = new RetailerService(Store, new FixedTimeProvider(Now), EmailSender, Images.ToUploadService());
        }

        public FakeEmailSender EmailSender { get; } = new();
        public FakeImageStorage Images { get; } = new();

        public Task<RetailerDetails> DeclareAsync(
            decimal revenue, decimal size, decimal capacity,
            bool includeMlor = true, bool submitForReview = true) =>
            Service.DeclareAsync(SalesUserId, RegionId, GroupId, "Cửa hàng thử nghiệm", "Nguyễn Văn An",
                "0909000000", "retailer.test@fscm.local", "Thành phố Hồ Chí Minh", RetailerTypes.GroceryStore,
                revenue, size, capacity, 4, 7, "Đánh giá thử nghiệm",
                10.776m, 106.700m, null,
                includeMlor ? [new RetailerMlorData(Category.Id, null, 0.35m, null)] : [],
                submitForReview, CancellationToken.None);
    }

    private sealed class FakeEmailSender : ITransactionalEmailSender
    {
        public List<RetailerAccountCreatedEmail> AccountCreatedEmails { get; } = [];
        public bool Fail { get; set; }

        public Task SendRetailerActivationOtpAsync(string email, string name, string otp, CancellationToken token) =>
            Task.CompletedTask;

        public Task SendRetailerAccountCreatedAsync(RetailerAccountCreatedEmail email, CancellationToken token)
        {
            if (Fail) throw new HttpRequestException("Brevo returned HTTP 503.");
            AccountCreatedEmails.Add(email);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : IRetailerStore
    {
        public List<Retailer> Retailers { get; } = [];
        public List<UserAccount> ContactAccounts { get; } = [];
        public List<RetailerDeclarationContact> DeclarationContacts { get; } = [];
        public List<RetailerSalesAssignment> Assignments { get; } = [];
        public List<MapLocation> Locations { get; } = [];
        public List<RetailerProductMlor> Mlors { get; } = [];
        public List<ProductCategory> Categories { get; } = [];
        public List<Product> Products { get; } = [];
        public List<SystemConfiguration> Configurations { get; } = [];
        public List<AuditLog> AuditLogs { get; } = [];
        public bool HasTerritoryAccess { get; set; }

        public Task<IReadOnlyCollection<RetailerView>> ListAsync(Guid actorUserId, string actorRole, string? status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<RetailerView>>(Retailers.Where(item => status is null || item.Status == status).Select(ToView).ToArray());
        public Task<RetailerView?> FindViewAsync(Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult(Retailers.Where(item => item.Id == retailerId).Select(ToView).SingleOrDefault());
        public Task<Retailer?> FindForUpdateAsync(Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult(Retailers.SingleOrDefault(item => item.Id == retailerId));
        public Task<UserAccount?> FindContactAccountForUpdateAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult(ContactAccounts.SingleOrDefault(item => item.Id == accountId));
        public Task<RetailerDeclarationContact?> FindDeclarationContactForUpdateAsync(Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult(DeclarationContacts.SingleOrDefault(item => item.RetailerId == retailerId));
        public Task<IReadOnlyList<UserAccount>> FindUnassignedRetailerAccountsAsync(
            string email, string phone, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserAccount>>(ContactAccounts.Where(account =>
                !account.IsActive &&
                (account.Email.Equals(email, StringComparison.OrdinalIgnoreCase) || account.Phone == phone) &&
                !Retailers.Any(retailer => retailer.UserAccountId == account.Id)).ToList());
        public Task<Retailer?> FindDuplicateAsync(
            string name, string? phone, string? email, string? address,
            Guid? excludedRetailerId, CancellationToken cancellationToken) =>
            Task.FromResult(Retailers.FirstOrDefault(item => item.Id != excludedRetailerId &&
                (ContactAccounts.Where(account => account.Id == item.UserAccountId).Any(account =>
                     (phone is not null && account.Phone?.ToLowerInvariant() == phone) ||
                     (email is not null && account.Email.ToLowerInvariant() == email)) ||
                 DeclarationContacts.Where(contact => contact.RetailerId == item.Id).Any(contact =>
                     (phone is not null && contact.Phone.ToLowerInvariant() == phone) ||
                     (email is not null && contact.Email.ToLowerInvariant() == email))) ||
                (address is not null && item.Id != excludedRetailerId && item.Name.ToLowerInvariant() == name &&
                    item.Address?.ToLowerInvariant() == address)));
        public Task<bool> ContactAccountExistsAsync(
            string phone, string email, Guid? excludedRetailerId, CancellationToken cancellationToken) =>
            Task.FromResult(ContactAccounts.Any(account =>
                !Retailers.Any(retailer => retailer.Id == excludedRetailerId && retailer.UserAccountId == account.Id) &&
                (account.Phone == phone || account.Email.Equals(email, StringComparison.OrdinalIgnoreCase))) ||
                DeclarationContacts.Any(contact => contact.RetailerId != excludedRetailerId &&
                    (contact.Phone == phone || contact.Email.Equals(email, StringComparison.OrdinalIgnoreCase))));
        public Task<IReadOnlyCollection<RetailerMlorView>> ListMlorDeclarationsAsync(
            IReadOnlyCollection<Guid> retailerIds, bool includeInactive, bool tracking, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<RetailerMlorView>>(Mlors
                .Where(item => retailerIds.Contains(item.RetailerId) && (includeInactive || item.IsActive))
                .Select(item =>
                {
                    var category = Categories.SingleOrDefault(value => value.Id == item.ProductCategoryId);
                    var product = Products.SingleOrDefault(value => value.Id == item.ProductId);
                    return new RetailerMlorView(item, category?.Code, category?.Name, product?.Sku, product?.Name);
                }).ToArray());
        public Task<ProductCategory?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.SingleOrDefault(item => item.Id == categoryId));
        public Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult(Products.SingleOrDefault(item => item.Id == productId));
        public Task<IReadOnlyCollection<ProductCategory>> ListCategoriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ProductCategory>>(Categories);
        public Task<SystemConfiguration?> FindCurrentConfigurationAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(Configurations.SingleOrDefault(item => item.ConfigKey == key && item.IsActive));
        public Task<bool> CanDeclareForGroupAsync(Guid userId, Guid regionId, Guid salesGroupId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<RetailerTerritoryAccess?> FindTerritoryAccessAsync(Guid userId, Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult(HasTerritoryAccess ? new RetailerTerritoryAccess(Assignments.Single(item => item.RetailerId == retailerId).SalesGroupId, "HCM-A") : null);
        public Task<bool> IsActiveSalesUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<string> NextDeclarationCodeAsync(CancellationToken cancellationToken) => Task.FromResult("KB-0100");
        public Task<string> NextBusinessCodeAsync(CancellationToken cancellationToken) => Task.FromResult("10005000");
        public void Add(Retailer retailer) => Retailers.Add(retailer);
        public void AddContactAccount(UserAccount userAccount) => ContactAccounts.Add(userAccount);
        public void AddDeclarationContact(RetailerDeclarationContact contact) => DeclarationContacts.Add(contact);
        public void RemoveDeclarationContact(RetailerDeclarationContact contact) => DeclarationContacts.Remove(contact);
        public void AddAssignment(RetailerSalesAssignment assignment) => Assignments.Add(assignment);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MapLocation?> FindLocationForUpdateAsync(Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult(Locations.SingleOrDefault(item => item.RetailerId == retailerId));
        public void AddLocation(MapLocation location) => Locations.Add(location);
        public void AddMlor(RetailerProductMlor mlor) => Mlors.Add(mlor);
        public void AddAuditLog(AuditLog auditLog) => AuditLogs.Add(auditLog);
        private RetailerView ToView(Retailer retailer)
        {
            var account = ContactAccounts.SingleOrDefault(item => item.Id == retailer.UserAccountId);
            var contact = DeclarationContacts.SingleOrDefault(item => item.RetailerId == retailer.Id);
            return new RetailerView(retailer, account?.FullName ?? contact!.ContactName,
                account?.Phone ?? contact!.Phone, account?.Email ?? contact!.Email,
                Assignments.Single(item => item.RetailerId == retailer.Id).SalesGroupId, "HCM-A",
                Locations.SingleOrDefault(item => item.RetailerId == retailer.Id));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
