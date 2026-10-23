using HotChocolate.Types;

namespace FSCM.API.GraphQL;

public sealed class DomainEntityObjectType<TEntity> : ObjectType<TEntity>
    where TEntity : class
{
    protected override void Configure(IObjectTypeDescriptor<TEntity> descriptor)
    {
        descriptor.Name($"{typeof(TEntity).Name}Entity");
        descriptor.Description($"Service-backed read-only entity view for {typeof(TEntity).Name}.");
        foreach (var property in typeof(TEntity).GetProperties().Where(property =>
                     property.Name.Contains("Hash", StringComparison.Ordinal) ||
                     property.Name.EndsWith("OperationKey", StringComparison.Ordinal)))
        {
            descriptor.Field(property).Ignore();
        }
    }
}
