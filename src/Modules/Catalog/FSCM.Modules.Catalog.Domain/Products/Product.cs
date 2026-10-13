using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;

namespace FSCM.Modules.Catalog.Domain.Products;

public enum ProductStatus { Draft, Published, Inactive }

/// <summary>Editable part of a product. Only a Draft can change it.</summary>
public sealed record ProductDetails(
    string Name, string CategoryCode, string Pack, int UnitsPerCase, int ShelfLifeDays, int NearExpiryDays,
    decimal BasePrice, decimal CostPrice, decimal DefaultMlorRatio, string? ImageUrl);

/// <summary>
/// Product / SKU. Lifecycle Draft → Published → Inactive. A published product is read-only because
/// orders, batches and promotions refer to it; to change it, clone it to a new SKU (new Draft).
/// </summary>
public sealed class Product : AggregateRoot<string>
{
    private Product() { }
    public Product(string sku, ProductDetails d, DateTime nowUtc, string? clonedFrom = null)
    {
        Guard.Against(!IdFormat.IsSku(sku), "product.sku", "Mã SKU dạng SKU000.");
        Id = sku; ClonedFromSku = clonedFrom; Status = ProductStatus.Draft; CreatedAtUtc = nowUtc;
        Apply(d);
    }
    public string Name { get; private set; } = "";
    public string CategoryCode { get; private set; } = "";
    public string Pack { get; private set; } = "";
    public int UnitsPerCase { get; private set; }
    /// <summary>SL – total shelf life in days.</summary>
    public int ShelfLifeDays { get; private set; }
    /// <summary>Days before expiry when a batch counts as near-expiry (date tracker).</summary>
    public int NearExpiryDays { get; private set; }
    public decimal BasePrice { get; private set; }
    public decimal CostPrice { get; private set; }
    /// <summary>m in MLOR = m × SL, used when the retailer has no own ratio for this SKU.</summary>
    public decimal DefaultMlorRatio { get; private set; }
    public string? ImageUrl { get; private set; }
    public ProductStatus Status { get; private set; }
    public string? ClonedFromSku { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }

    public ProductDetails Details => new(Name, CategoryCode, Pack, UnitsPerCase, ShelfLifeDays, NearExpiryDays, BasePrice, CostPrice, DefaultMlorRatio, ImageUrl);

    public void UpdateDraft(ProductDetails d)
    {
        Guard.Against(Status != ProductStatus.Draft, "product.readonly", $"{Id} đã publish – chỉ đọc. Dùng \"Nhân bản sang SKU mới\" để thay đổi.");
        Apply(d);
    }

    public void Publish(DateTime nowUtc)
    {
        Guard.Against(Status != ProductStatus.Draft, "product.state", "Chỉ publish sản phẩm ở trạng thái Nháp.");
        Guard.Against(BasePrice <= 0 || CostPrice <= 0, "product.price", "Giá bán và giá vốn phải lớn hơn 0.");
        Status = ProductStatus.Published; PublishedAtUtc = nowUtc;
    }

    public void Inactivate()
    {
        Guard.Against(Status != ProductStatus.Published, "product.state", "Chỉ ngừng kinh doanh sản phẩm đã publish.");
        Status = ProductStatus.Inactive;
    }

    /// <summary>"Nhân bản sang SKU mới": copies the details into a new Draft. The original stays unchanged.</summary>
    public Product CloneAs(string newSku, DateTime nowUtc) => new(newSku, Details, nowUtc, Id);

    private void Apply(ProductDetails d)
    {
        Guard.Against(string.IsNullOrWhiteSpace(d.Name), "product.name", "Tên sản phẩm bắt buộc.");
        Guard.Against(d.ShelfLifeDays <= 0, "product.shelf_life", "Hạn dùng (ngày) phải lớn hơn 0.");
        Guard.Against(d.NearExpiryDays < 0 || d.NearExpiryDays >= d.ShelfLifeDays, "product.near_expiry", "Ngưỡng cận hạn phải nhỏ hơn hạn dùng.");
        Guard.Against(d.DefaultMlorRatio <= 0 || d.DefaultMlorRatio >= 1, "product.mlor", "Tỷ lệ MLOR mặc định trong khoảng (0, 1).");
        Guard.Against(d.BasePrice < 0 || d.CostPrice < 0 || d.UnitsPerCase <= 0, "product.values", "Giá và quy cách không hợp lệ.");
        Name = d.Name.Trim(); CategoryCode = d.CategoryCode; Pack = d.Pack; UnitsPerCase = d.UnitsPerCase; ShelfLifeDays = d.ShelfLifeDays;
        NearExpiryDays = d.NearExpiryDays; BasePrice = d.BasePrice; CostPrice = d.CostPrice; DefaultMlorRatio = d.DefaultMlorRatio; ImageUrl = d.ImageUrl;
    }
}
