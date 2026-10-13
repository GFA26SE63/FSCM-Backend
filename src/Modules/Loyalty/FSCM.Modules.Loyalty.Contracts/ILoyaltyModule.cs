using FSCM.BuildingBlocks.Application;

namespace FSCM.Modules.Loyalty.Contracts;

/// <param name="Available">Earned points that can be redeemed.</param>
/// <param name="Pending">Points from delivered credit orders that are not paid yet – cannot be redeemed.</param>
/// <param name="Held">Points reserved by gift requests that are not finished.</param>
public sealed record LoyaltyBalanceDto(string RetailerCode, int Available, int Pending, int Held);

public interface ILoyaltyModule
{
    Task<LoyaltyBalanceDto> BalanceAsync(string retailerCode, CancellationToken ct = default);
}

public sealed record GiftRedemptionRequested(string RedemptionCode, string RetailerCode, string GiftCode, string GiftName, int Points) : IntegrationEvent;
public sealed record GiftRedemptionApproved(string RedemptionCode, string RetailerCode, string GiftName) : IntegrationEvent;
public sealed record GiftRedemptionRejected(string RedemptionCode, string RetailerCode, string GiftName, int PointsRefunded, string Reason) : IntegrationEvent;
public sealed record GiftRedemptionDelivered(string RedemptionCode, string RetailerCode, string GiftName) : IntegrationEvent;
