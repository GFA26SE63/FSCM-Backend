namespace FSCM.Application.Features.AllocationRuns;

/// <summary>One allocated part of an order line, with every component of its price.</summary>
public sealed record OrderPricePartResult(
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    decimal Quantity,
    decimal ListUnitPrice,
    decimal BaseNetUnitPrice,
    DateTimeOffset PricedAt,
    DateTimeOffset BatchExpiresAt,
    int TotalShelfLifeHours,
    decimal RemainingShelfLifePercent,
    Guid? ShelfLifeDiscountTierId,
    string? DiscountType,
    decimal? DiscountValue,
    decimal ShelfLifeDiscountPerUnit,
    decimal FinalNetUnitPrice,
    decimal Amount,
    int DiscountTableVersion,
    string Explanation);

public sealed record OrderPriceLineResult(
    Guid SalesOrderLineId,
    Guid AllocationRunLineId,
    Guid ProductId,
    string Sku,
    int LineNumber,
    decimal OrderedQuantity,
    decimal AllocatedQuantity,
    decimal BaseAmount,
    decimal ShelfLifeDiscountAmount,
    decimal FinalAmount,
    IReadOnlyCollection<OrderPricePartResult> Parts);

public sealed record OrderPriceResult(
    Guid SalesOrderId,
    string OrderCode,
    Guid AllocationRunId,
    string RunCode,
    Guid AllocationPlanVersionId,
    int PlanVersionNumber,
    string PlanVersionStatus,
    bool IsApproved,
    DateTimeOffset PricedAt,
    decimal BaseAmount,
    decimal ShelfLifeDiscountAmount,
    decimal FinalAmount,
    IReadOnlyCollection<OrderPriceLineResult> Lines);

/// <summary>
/// Reads back the price a run settled on, for traces, collection, complaints and loyalty points.
///
/// Every one of those reads the same stored record instead of recomputing. A refund worked out
/// from a freshly calculated price would not match the invoice the moment a discount table
/// changed, and nobody would notice until a reconciliation.
///
/// The status of the plan version travels with the answer. A price from a proposal still awaiting
/// approval is a quotation, not something to collect against, and a caller that cannot tell the
/// two apart will eventually charge against one.
/// </summary>
public sealed class AllocationPriceLookupService(IAllocationRunStore store)
{
    public async Task<OrderPriceResult> GetByOrderAsync(Guid salesOrderId, CancellationToken cancellationToken)
    {
        var view = await store.FindOrderPriceAsync(salesOrderId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound(
                "This order has no priced allocation; it was never allocated by a run.");

        var lines = view.Lines
            .OrderBy(line => line.Line.LineNumber)
            .Select(line =>
            {
                var parts = view.Prices
                    .Where(price => price.Price.AllocationRunLineId == line.Line.Id)
                    .OrderBy(price => price.BatchCode, StringComparer.Ordinal)
                    .Select(price => new OrderPricePartResult(
                        price.Price.BatchId,
                        price.BatchCode,
                        price.Price.WarehouseId,
                        price.Price.Quantity,
                        price.Price.ListUnitPrice,
                        price.Price.BaseNetUnitPrice,
                        price.Price.PricedAt,
                        price.Price.BatchExpiresAt,
                        price.Price.TotalShelfLifeHours,
                        price.Price.RemainingShelfLifePercent,
                        price.Price.ShelfLifeDiscountTierId,
                        price.Price.DiscountType,
                        price.Price.DiscountValue,
                        price.Price.ShelfLifeDiscountPerUnit,
                        price.Price.FinalNetUnitPrice,
                        price.Price.Amount,
                        price.Price.DiscountTableVersion,
                        price.Price.Explanation))
                    .ToArray();

                return new OrderPriceLineResult(
                    line.Line.SalesOrderLineId,
                    line.Line.Id,
                    line.Line.ProductId,
                    line.Line.SkuSnapshot,
                    line.Line.LineNumber,
                    line.Line.OrderedQuantity,
                    line.Line.AllocatedQuantity,
                    Round(parts.Sum(part => part.BaseNetUnitPrice * part.Quantity)),
                    Round(parts.Sum(part => part.ShelfLifeDiscountPerUnit * part.Quantity)),
                    parts.Sum(part => part.Amount),
                    parts);
            })
            .ToArray();

        EnsureLinesReconcile(view.OrderCode, lines);

        return new OrderPriceResult(
            salesOrderId,
            view.OrderCode,
            view.AllocationRunId,
            view.RunCode,
            view.PlanVersion.Id,
            view.PlanVersion.VersionNumber,
            view.PlanVersion.Status,
            view.PlanVersion.Status == AllocationPlanVersionStatuses.Approved,
            view.PlanVersion.PricedAt,
            Round(lines.Sum(line => line.BaseAmount)),
            Round(lines.Sum(line => line.ShelfLifeDiscountAmount)),
            Round(lines.Sum(line => line.FinalAmount)),
            lines);
    }

    public async Task<OrderPriceLineResult> GetByOrderLineAsync(
        Guid salesOrderLineId, CancellationToken cancellationToken)
    {
        var order = await store.FindOrderIdForLineAsync(salesOrderLineId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound(
                "This order line has no priced allocation.");
        var priced = await GetByOrderAsync(order, cancellationToken);
        return priced.Lines.SingleOrDefault(line => line.SalesOrderLineId == salesOrderLineId)
            ?? throw AllocationRunOperationException.NotFound("This order line has no priced allocation.");
    }

    /// <summary>
    /// Checks the stored record against itself before serving it.
    ///
    /// Two things were written by different steps and must still agree: the quantity the run
    /// recorded on the line, and the quantities of the price rows that cover it. And within a price
    /// row, the components must still multiply out to the amount stored beside them. Either kind of
    /// mismatch means the record is no longer a faithful copy of the plan, which is worth refusing
    /// rather than quietly passing on to an invoice.
    /// </summary>
    private static void EnsureLinesReconcile(string orderCode, IReadOnlyCollection<OrderPriceLineResult> lines)
    {
        foreach (var line in lines)
        {
            var partQuantity = line.Parts.Sum(part => part.Quantity);
            if (partQuantity != line.AllocatedQuantity)
            {
                throw AllocationRunOperationException.Conflict(
                    $"Order {orderCode} line {line.LineNumber}: priced parts cover {partQuantity:0.###} " +
                    $"but the run allocated {line.AllocatedQuantity:0.###}.");
            }

            foreach (var part in line.Parts)
            {
                var expectedUnitPrice = part.BaseNetUnitPrice - part.ShelfLifeDiscountPerUnit;
                if (part.FinalNetUnitPrice != expectedUnitPrice)
                {
                    throw AllocationRunOperationException.Conflict(
                        $"Order {orderCode} line {line.LineNumber}, batch {part.BatchCode}: unit price " +
                        $"{part.FinalNetUnitPrice:0.##} does not match base less reduction {expectedUnitPrice:0.##}.");
                }

                var expectedAmount = Round(part.FinalNetUnitPrice * part.Quantity);
                if (Round(part.Amount) != expectedAmount)
                {
                    throw AllocationRunOperationException.Conflict(
                        $"Order {orderCode} line {line.LineNumber}, batch {part.BatchCode}: amount " +
                        $"{part.Amount:0.##} does not match {expectedAmount:0.##} for its unit price and quantity.");
                }
            }
        }
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
