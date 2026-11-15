using FSCM.Application.Features.Catalog;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Catalog;

public sealed class CategoryDefaultMlorTests
{
    private static ProductCategory Category(Guid id, Guid? parent, decimal? ratio, int version = 1) => new()
    {
        Id = id, ParentCategoryId = parent, Code = id.ToString("N")[..6], Name = "Cat",
        IsActive = true, DefaultMlorRatio = ratio, DefaultMlorVersion = ratio is null ? 0 : version
    };

    [Fact]
    public void Uses_own_category_default_first()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var map = new[] { Category(root, null, 0.2m), Category(child, root, 0.4m) }.ToDictionary(c => c.Id);
        Assert.Equal(0.4m, CategoryDefaultMlor.Resolve(child, map)!.DefaultMlorRatio);
    }

    [Fact]
    public void Falls_back_to_nearest_ancestor_with_a_default()
    {
        var root = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var leaf = Guid.NewGuid();
        var map = new[] { Category(root, null, 0.2m), Category(middle, root, null), Category(leaf, middle, null) }
            .ToDictionary(c => c.Id);
        Assert.Equal(root, CategoryDefaultMlor.Resolve(leaf, map)!.Id);
    }

    [Fact]
    public void Returns_null_when_no_category_in_the_chain_has_a_default()
    {
        var root = Guid.NewGuid();
        var leaf = Guid.NewGuid();
        var map = new[] { Category(root, null, null), Category(leaf, root, null) }.ToDictionary(c => c.Id);
        Assert.Null(CategoryDefaultMlor.Resolve(leaf, map));
    }

    [Fact]
    public void Survives_a_cycle_in_the_category_tree()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var map = new[] { Category(a, b, null), Category(b, a, null) }.ToDictionary(c => c.Id);
        Assert.Null(CategoryDefaultMlor.Resolve(a, map));
    }
}
