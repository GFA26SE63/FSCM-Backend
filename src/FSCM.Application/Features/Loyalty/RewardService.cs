using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Loyalty;

public sealed class RewardService(IRewardStore store, TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<RewardResult>> ListAsync(
        Guid? programId, bool includeInactive, CancellationToken cancellationToken) =>
        (await store.ListAsync(programId, includeInactive, cancellationToken)).Select(ToResult).ToArray();

    public async Task<RewardResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResult(await store.FindAsync(id, cancellationToken)
            ?? throw LoyaltyOperationException.NotFound("Loyalty reward was not found."));

    public async Task<RewardResult> CreateAsync(CreateRewardData data, CancellationToken cancellationToken)
    {
        await ValidateAsync(data.LoyaltyProgramId, data.Name, data.RewardType,
            data.RequiredPoints, data.DiscountAmount, data.DiscountPercent,
            data.AvailableQuantity, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var reward = new LoyaltyReward
        {
            Id = Guid.NewGuid(), LoyaltyProgramId = data.LoyaltyProgramId,
            Code = await store.NextCodeAsync(cancellationToken), Name = data.Name.Trim(),
            Description = Normalize(data.Description), ImageUrl = Normalize(data.ImageUrl),
            RewardType = data.RewardType.Trim(), RequiredPoints = data.RequiredPoints,
            DiscountAmount = data.DiscountAmount, DiscountPercent = data.DiscountPercent,
            AvailableQuantity = data.AvailableQuantity, IsActive = false,
            CreatedAt = now, UpdatedAt = now
        };
        store.Add(reward);
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(reward);
    }

    public async Task<RewardResult> UpdateAsync(Guid id, UpdateRewardData data, CancellationToken cancellationToken)
    {
        var reward = await store.FindAsync(id, cancellationToken)
            ?? throw LoyaltyOperationException.NotFound("Loyalty reward was not found.");
        await ValidateAsync(reward.LoyaltyProgramId, data.Name, data.RewardType,
            data.RequiredPoints, data.DiscountAmount, data.DiscountPercent,
            data.AvailableQuantity, cancellationToken);
        reward.Name = data.Name.Trim(); reward.Description = Normalize(data.Description);
        reward.ImageUrl = Normalize(data.ImageUrl); reward.RewardType = data.RewardType.Trim();
        reward.RequiredPoints = data.RequiredPoints; reward.DiscountAmount = data.DiscountAmount;
        reward.DiscountPercent = data.DiscountPercent; reward.AvailableQuantity = data.AvailableQuantity;
        reward.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(reward);
    }

    public Task<RewardResult> ActivateAsync(Guid id, CancellationToken cancellationToken) => SetActiveAsync(id, true, cancellationToken);
    public Task<RewardResult> DeactivateAsync(Guid id, CancellationToken cancellationToken) => SetActiveAsync(id, false, cancellationToken);

    private async Task<RewardResult> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var reward = await store.FindAsync(id, cancellationToken)
            ?? throw LoyaltyOperationException.NotFound("Loyalty reward was not found.");
        if (active && reward.AvailableQuantity is 0)
            throw LoyaltyOperationException.Conflict("An out-of-stock reward cannot be activated.");
        if (reward.IsActive != active)
        {
            reward.IsActive = active; reward.UpdatedAt = timeProvider.GetUtcNow();
            await store.SaveChangesAsync(cancellationToken);
        }
        return ToResult(reward);
    }

    private async Task ValidateAsync(Guid programId, string name, string rewardType,
        int requiredPoints, decimal? discountAmount, decimal? discountPercent,
        int? availableQuantity, CancellationToken cancellationToken)
    {
        if (await store.FindProgramAsync(programId, cancellationToken) is null)
            throw LoyaltyOperationException.NotFound("Loyalty program was not found.");
        if (name.Trim().Length is < 2 or > 200)
            throw LoyaltyOperationException.Validation("Reward name must contain between 2 and 200 characters.");
        if (string.IsNullOrWhiteSpace(rewardType))
            throw LoyaltyOperationException.Validation("Reward type is required.");
        if (requiredPoints <= 0)
            throw LoyaltyOperationException.Validation("Required points must be greater than zero.");
        if (discountAmount is < 0 || discountPercent is < 0 or > 100)
            throw LoyaltyOperationException.Validation("Discount values are invalid.");
        if (availableQuantity is < 0)
            throw LoyaltyOperationException.Validation("Available quantity cannot be negative.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static RewardResult ToResult(LoyaltyReward item) => new(item.Id, item.LoyaltyProgramId,
        item.Code, item.Name, item.Description, item.ImageUrl, item.RewardType, item.RequiredPoints,
        item.DiscountAmount, item.DiscountPercent, item.AvailableQuantity, item.IsActive,
        item.CreatedAt, item.UpdatedAt);
}
