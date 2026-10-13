using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Catalog.Domain.Categories;

/// <summary>Product category CAT-00 (Sữa chua, Sữa tươi, Bánh tươi…). Promotions can target a whole category.</summary>
public sealed class Category : AggregateRoot<string>
{
    private Category() { }
    public Category(string code, string name)
    {
        Guard.Against(!code.StartsWith("CAT-", StringComparison.Ordinal), "category.code", "Mã danh mục dạng CAT-00.");
        Id = code; Rename(name); Active = true;
    }
    public string Name { get; private set; } = "";
    public bool Active { get; private set; }
    public void Rename(string name) { Guard.Against(string.IsNullOrWhiteSpace(name), "category.name", "Tên danh mục bắt buộc."); Name = name.Trim(); }
    public void SetActive(bool active) => Active = active;
}
