using HotChocolate.Types;
using HotChocolate.Types.Descriptors;

namespace FSCM.API.GraphQL;

/// <summary>
/// Keeps direct domain entity types separate from the existing Application result
/// types that intentionally use the public names Product, Warehouse, and similar.
/// </summary>
public sealed class EntityNamingConventions : DefaultNamingConventions
{
    public override string GetTypeName(Type type) =>
        IsDomainEntity(type) ? $"{type.Name}Entity" : base.GetTypeName(type);

    public override string GetTypeName(Type type, TypeKind kind) =>
        IsDomainEntity(type) ? $"{type.Name}Entity" : base.GetTypeName(type, kind);

    public override string GetTypeName(string typeName, TypeKind kind) =>
        typeName.EndsWith("Entity", StringComparison.Ordinal)
            ? typeName
            : base.GetTypeName(typeName, kind);

    private static bool IsDomainEntity(Type type) =>
        type.Namespace == "FSCM.Domain.Entities";
}
