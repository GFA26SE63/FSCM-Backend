using HotChocolate.Types;
using HotChocolate.Data;
using FSCM.Domain.Entities;
using FSCM.Application.Features.Storage;

namespace FSCM.API.GraphQL;

public sealed class DomainEntityObjectType<TEntity> : ObjectType<TEntity>
    where TEntity : class
{
    protected override void Configure(IObjectTypeDescriptor<TEntity> descriptor)
    {
        descriptor.Name($"{typeof(TEntity).Name}Entity");
        descriptor.Description($"Service-backed read-only entity view for {typeof(TEntity).Name}.");
        if (typeof(TEntity) == typeof(LoyaltyReward))
        {
            // Signing a stored reference is local work, not another detail/database request per reward.
            descriptor.Field(typeof(TEntity).GetProperty(nameof(LoyaltyReward.ImageUrl))!).IsProjected();
            descriptor.Field("resolvedImageUrl").Type<StringType>().Resolve(context =>
            {
                var reference = ((LoyaltyReward)(object)context.Parent<TEntity>()).ImageUrl;
                return reference?.StartsWith("reward/", StringComparison.Ordinal) == true
                    ? context.Service<ImageUploadService>().GetReadUrl(ImageCategory.LoyaltyRewardImage, reference)
                    : reference;
            });
        }
        foreach (var property in typeof(TEntity).GetProperties().Where(property =>
                     property.Name.Contains("Hash", StringComparison.Ordinal) ||
                     property.Name.EndsWith("OperationKey", StringComparison.Ordinal)))
        {
            descriptor.Field(property).Ignore();
        }
    }
}
