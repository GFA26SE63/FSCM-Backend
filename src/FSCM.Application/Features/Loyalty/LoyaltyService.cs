using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Loyalty;

public sealed class LoyaltyService(ILoyaltyStore store, TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<LoyaltyProgramResult>> ListAsync(
        bool includeInactive, CancellationToken cancellationToken) =>
        await Task.WhenAll((await store.ListProgramsAsync(includeInactive, cancellationToken))
            .Select(program => ToResultAsync(program, cancellationToken)));

    public async Task<LoyaltyProgramResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await ToResultAsync(await store.FindProgramAsync(id, cancellationToken)
            ?? throw LoyaltyOperationException.NotFound("Loyalty program was not found."), cancellationToken);

    public async Task<LoyaltyProgramResult> CreateAsync(
        CreateLoyaltyProgramData data, Guid actorUserId, CancellationToken cancellationToken)
    {
        var retailerIds = await ValidateAsync(data.Name, data.SpendAmountPerPoint, data.MinimumRedeemPoints,
            data.StartAt, data.EndAt, data.PointsExpireAt, data.RetailerIds, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var program = new LoyaltyProgram
        {
            Id = Guid.NewGuid(),
            Code = await store.NextProgramCodeAsync(cancellationToken),
            Name = data.Name.Trim(),
            Description = NormalizeDescription(data.Description),
            SpendAmountPerPoint = data.SpendAmountPerPoint,
            MinimumRedeemPoints = data.MinimumRedeemPoints,
            StartAt = data.StartAt.ToUniversalTime(),
            EndAt = data.EndAt.ToUniversalTime(),
            PointsExpireAt = data.PointsExpireAt?.ToUniversalTime(),
            IsActive = false,
            CreatedByUserId = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddProgram(program);
        store.AddProgramRetailers(retailerIds.Select(retailerId => new LoyaltyProgramRetailer
        {
            Id = Guid.NewGuid(), LoyaltyProgramId = program.Id, RetailerId = retailerId
        }));
        await store.SaveChangesAsync(cancellationToken);
        return await ToResultAsync(program, cancellationToken);
    }

    public async Task<LoyaltyProgramResult> UpdateAsync(
        Guid id, UpdateLoyaltyProgramData data, CancellationToken cancellationToken)
    {
        var retailerIds = await ValidateAsync(data.Name, data.SpendAmountPerPoint, data.MinimumRedeemPoints,
            data.StartAt, data.EndAt, data.PointsExpireAt, data.RetailerIds, cancellationToken);
        var program = await store.FindProgramAsync(id, cancellationToken)
            ?? throw LoyaltyOperationException.NotFound("Loyalty program was not found.");
        program.Name = data.Name.Trim();
        program.Description = NormalizeDescription(data.Description);
        program.SpendAmountPerPoint = data.SpendAmountPerPoint;
        program.MinimumRedeemPoints = data.MinimumRedeemPoints;
        program.StartAt = data.StartAt.ToUniversalTime();
        program.EndAt = data.EndAt.ToUniversalTime();
        program.PointsExpireAt = data.PointsExpireAt?.ToUniversalTime();
        program.UpdatedAt = timeProvider.GetUtcNow();
        var oldRetailerIds = await store.ListProgramRetailerIdsAsync(id, cancellationToken);
        store.RemoveProgramRetailers(oldRetailerIds, id);
        store.AddProgramRetailers(retailerIds.Select(retailerId => new LoyaltyProgramRetailer
        {
            Id = Guid.NewGuid(), LoyaltyProgramId = id, RetailerId = retailerId
        }));
        await store.SaveChangesAsync(cancellationToken);
        return await ToResultAsync(program, cancellationToken);
    }

    public Task<LoyaltyProgramResult> ActivateAsync(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, true, cancellationToken);

    public Task<LoyaltyProgramResult> DeactivateAsync(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, false, cancellationToken);

    private async Task<LoyaltyProgramResult> SetActiveAsync(
        Guid id, bool active, CancellationToken cancellationToken)
    {
        var program = await store.FindProgramAsync(id, cancellationToken)
            ?? throw LoyaltyOperationException.NotFound("Loyalty program was not found.");
        if (active && program.EndAt < timeProvider.GetUtcNow())
            throw LoyaltyOperationException.Conflict("An expired loyalty program cannot be activated.");
        if (program.IsActive != active)
        {
            program.IsActive = active;
            program.UpdatedAt = timeProvider.GetUtcNow();
            await store.SaveChangesAsync(cancellationToken);
        }
        return await ToResultAsync(program, cancellationToken);
    }

    private static string? NormalizeDescription(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<Guid[]> ValidateAsync(string name, decimal spendAmountPerPoint,
        int minimumRedeemPoints, DateTimeOffset startAt, DateTimeOffset endAt,
        DateTimeOffset? pointsExpireAt, IReadOnlyCollection<Guid> retailerIds,
        CancellationToken cancellationToken)
    {
        if (pointsExpireAt.HasValue && pointsExpireAt.Value < endAt)
            throw LoyaltyOperationException.Validation("Point expiry must be on or after program end.");
        var distinct = retailerIds.Distinct().ToArray();
        if (distinct.Length == 0)
            throw LoyaltyOperationException.Validation("At least one retailer must be selected.");
        if ((await store.FindExistingRetailerIdsAsync(distinct, cancellationToken)).Count != distinct.Length)
            throw LoyaltyOperationException.NotFound("One or more selected retailers were not found.");
        Validate(name, spendAmountPerPoint, minimumRedeemPoints, startAt, endAt);
        return distinct;
    }

    private static void Validate(string name, decimal spendAmountPerPoint,
        int minimumRedeemPoints, DateTimeOffset startAt, DateTimeOffset endAt)
    {
        if (name.Trim().Length is < 3 or > 200)
            throw LoyaltyOperationException.Validation("Loyalty program name must contain between 3 and 200 characters.");
        if (spendAmountPerPoint <= 0)
            throw LoyaltyOperationException.Validation("Spend amount per point must be greater than zero.");
        if (minimumRedeemPoints < 0)
            throw LoyaltyOperationException.Validation("Minimum redeem points cannot be negative.");
        if (endAt <= startAt)
            throw LoyaltyOperationException.Validation("Validity end must be later than validity start.");
    }

    private async Task<LoyaltyProgramResult> ToResultAsync(
        LoyaltyProgram program, CancellationToken cancellationToken) => new(
        program.Id, program.Code, program.Name, program.Description,
        program.SpendAmountPerPoint, program.MinimumRedeemPoints,
        program.StartAt, program.EndAt, program.PointsExpireAt,
        await store.ListProgramRetailerIdsAsync(program.Id, cancellationToken), program.IsActive,
        program.CreatedByUserId, program.CreatedAt, program.UpdatedAt);
}
