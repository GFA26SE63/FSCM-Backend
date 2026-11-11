using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Payments;

public sealed class RetailerDebtPayOsService(
    IRetailerDebtPayOsStore store,
    IPayOsGateway payOsGateway,
    TimeProvider timeProvider,
    OrderCompletionService? completionService = null)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<RetailerDebtPayOsCheckoutResult> CreateAsync(
        CreateRetailerDebtPayOsCheckoutData data,
        string operationKey,
        Guid retailerUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        if (actorRole != SystemRoles.Retailer)
            throw PaymentOperationException.Forbidden("Only a Retailer can create a debt checkout.");
        operationKey = NormalizeKey(operationKey);
        var mode = NormalizeMode(data.Mode);
        var ids = NormalizeIds(mode, data.PaymentIds);
        var requestHash = HashRequest(mode, ids);
        var replay = await store.FindByOperationKeyAsync(operationKey, false, cancellationToken);
        if (replay is not null)
        {
            if (replay.Checkout.RequestHash != requestHash)
                throw PaymentOperationException.Conflict("Idempotency key was already used for another debt checkout request.");
            return ToResult(replay);
        }

        var retailer = await store.FindRetailerByUserAsync(retailerUserId, cancellationToken)
            ?? throw PaymentOperationException.Forbidden("Retailer account was not found for the signed-in user.");
        var selected = await store.ListCollectibleForUpdateAsync(
            retailer.Id, mode == RetailerDebtCheckoutModes.All ? null : ids, cancellationToken);
        if (mode == RetailerDebtCheckoutModes.Selected && selected.Count != ids.Length)
            throw PaymentOperationException.Conflict("One or more selected receivables are not collectible or already have a pending checkout.");
        if (selected.Count == 0)
            throw PaymentOperationException.Conflict("There is no collectible receivable to pay.");
        var snapshots = selected.Select(x => new
        {
            View = x,
            Amount = x.Payment.AdjustedAmount - x.Payment.ConfirmedAmount - x.OpenDisputedAmount
        }).OrderBy(x => x.View.Order.OrderCode, StringComparer.Ordinal).ToArray();
        if (snapshots.Any(x => x.Amount <= 0))
            throw PaymentOperationException.Conflict("A selected receivable no longer has a collectible balance.");
        var total = snapshots.Sum(x => x.Amount);
        var payOsOrderCode = CreatePayOsOrderCode(Guid.NewGuid());
        var link = await payOsGateway.CreatePaymentLinkAsync(new(
            payOsOrderCode, ToPayOsAmount(total), $"FSCM CONG NO {snapshots.Length}", retailer.Name), cancellationToken);
        if (link.OrderCode != payOsOrderCode || link.Amount != ToPayOsAmount(total) ||
            string.IsNullOrWhiteSpace(link.PaymentLinkId) || string.IsNullOrWhiteSpace(link.CheckoutUrl))
            throw PaymentOperationException.Gateway("payOS returned an inconsistent debt payment link.");

        await store.ExecuteInTransactionAsync(async token =>
        {
            var existing = await store.FindByOperationKeyAsync(operationKey, true, token);
            if (existing is not null)
            {
                if (existing.Checkout.RequestHash != requestHash)
                    throw PaymentOperationException.Conflict("Idempotency key was already used for another debt checkout request.");
                return;
            }
            var current = await store.ListCollectibleForUpdateAsync(
                retailer.Id, snapshots.Select(x => x.View.Payment.Id).ToArray(), token);
            if (current.Count != snapshots.Length)
                throw PaymentOperationException.Conflict("A receivable changed while the checkout was being created. Refresh and retry.");
            foreach (var snapshot in snapshots)
            {
                var item = current.Single(x => x.Payment.Id == snapshot.View.Payment.Id);
                var collectible = item.Payment.AdjustedAmount - item.Payment.ConfirmedAmount - item.OpenDisputedAmount;
                if (collectible != snapshot.Amount)
                    throw PaymentOperationException.Conflict("A receivable balance changed while the checkout was being created. Refresh and retry.");
            }
            var now = timeProvider.GetUtcNow();
            var checkout = new RetailerDebtPayOsCheckout
            {
                Id = Guid.NewGuid(), RetailerId = retailer.Id, Amount = total,
                Status = RetailerDebtPayOsStatuses.Pending, PayOsOrderCode = link.OrderCode,
                PaymentLinkId = link.PaymentLinkId, CheckoutUrl = link.CheckoutUrl,
                QrCode = link.QrCode, PayOsExpiredAt = link.ExpiredAt,
                OperationKey = operationKey, RequestHash = requestHash, Version = 1,
                CreatedAt = now, UpdatedAt = now
            };
            store.AddCheckout(checkout);
            store.AddAllocations(snapshots.Select(x => new RetailerDebtPayOsAllocation
            {
                Id = Guid.NewGuid(), CheckoutId = checkout.Id,
                PaymentId = x.View.Payment.Id, Amount = x.Amount
            }));
            store.AddAuditLog(new AuditLog
            {
                Id = Guid.NewGuid(), UserAccountId = retailerUserId,
                EntityName = nameof(RetailerDebtPayOsCheckout), EntityId = checkout.Id,
                Action = "Created", NewValues = JsonSerializer.Serialize(new { checkout.Amount, Count = snapshots.Length }),
                Reason = "Retailer tạo checkout PayOS thanh toán công nợ", CreatedAt = now
            });
            await store.SaveChangesAsync(token);
        }, cancellationToken);
        store.ClearTracking();
        return ToResult((await store.FindByOperationKeyAsync(operationKey, false, cancellationToken))!);
    }

    public async Task<RetailerDebtPayOsCheckoutResult> GetAsync(
        Guid checkoutId, Guid retailerUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (actorRole != SystemRoles.Retailer)
            throw PaymentOperationException.Forbidden("Only a Retailer can view a debt checkout.");
        var retailer = await store.FindRetailerByUserAsync(retailerUserId, cancellationToken)
            ?? throw PaymentOperationException.Forbidden("Retailer account was not found for the signed-in user.");
        var view = await store.FindByIdForRetailerAsync(checkoutId, retailer.Id, cancellationToken)
            ?? throw PaymentOperationException.NotFound("Debt checkout was not found.");
        if (view.Checkout.Status == RetailerDebtPayOsStatuses.Pending)
        {
            await ReconcilePendingCheckoutAsync(view.Checkout, cancellationToken);
            view = await store.FindByIdForRetailerAsync(checkoutId, retailer.Id, cancellationToken) ?? view;
        }
        return ToResult(view);
    }

    public Task<RetailerDebtCheckoutView?> FindByPayOsOrderCodeAsync(
        long orderCode, CancellationToken cancellationToken) =>
        store.FindByPayOsOrderCodeAsync(orderCode, false, cancellationToken);

    public async Task<PayOsWebhookResult> HandleVerifiedWebhookAsync(
        PayOsVerifiedWebhook verified, CancellationToken cancellationToken)
    {
        var existing = await store.FindByPayOsOrderCodeAsync(verified.Data.OrderCode, false, cancellationToken);
        if (existing is null) return new(false, "Webhook does not match an FSCM debt checkout.", null);
        var successful = verified.Success && verified.Code == "00" && verified.Data.Code == "00";
        if (!successful)
        {
            await store.ExecuteInTransactionAsync(async token =>
            {
                var current = await store.FindByPayOsOrderCodeAsync(verified.Data.OrderCode, true, token)
                    ?? throw PaymentOperationException.NotFound("Debt checkout was not found.");
                if (current.Checkout.Status != RetailerDebtPayOsStatuses.Pending) return;
                current.Checkout.Status = IsExpired(current.Checkout) ||
                    verified.Data.Description2.Contains("expire", StringComparison.OrdinalIgnoreCase)
                    ? RetailerDebtPayOsStatuses.Expired : RetailerDebtPayOsStatuses.Failed;
                current.Checkout.Version++;
                current.Checkout.UpdatedAt = timeProvider.GetUtcNow();
                await store.SaveChangesAsync(token);
            }, cancellationToken);
            return new(true, "Debt checkout failure status was recorded.", null);
        }

        Guid[] affectedOrderIds = [];
        Guid? receiptId = null;
        await store.ExecuteInTransactionAsync(async token =>
        {
            var current = await store.FindByPayOsOrderCodeAsync(verified.Data.OrderCode, true, token)
                ?? throw PaymentOperationException.NotFound("Debt checkout was not found.");
            ValidateWebhook(current, verified.Data);
            var hash = HashWebhook(verified.Data);
            if (current.Checkout.Status == RetailerDebtPayOsStatuses.Paid)
            {
                if (current.Checkout.PayOsReference == verified.Data.Reference && current.Checkout.WebhookHash == hash)
                { receiptId = current.Checkout.PaymentReceiptId; return; }
                throw PaymentOperationException.Conflict("Debt checkout was already paid by another transaction.");
            }
            if (current.Checkout.Status != RetailerDebtPayOsStatuses.Pending)
                throw PaymentOperationException.Conflict("Debt checkout is no longer payable.");
            var now = timeProvider.GetUtcNow();
            var paidAt = ParseTransactionTime(verified.Data.TransactionDateTime, now);
            foreach (var allocation in current.Allocations)
            {
                var payment = current.Payments.Single(x => x.Payment.Id == allocation.PaymentId);
                var collectible = payment.Payment.AdjustedAmount - payment.Payment.ConfirmedAmount - payment.OpenDisputedAmount;
                if (collectible < allocation.Amount)
                    throw PaymentOperationException.Conflict("A snapshotted receivable no longer has enough collectible balance.");
            }
            var receipt = new PaymentReceipt
            {
                Id = Guid.NewGuid(), ReceiptCode = $"PT-{now:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                RetailerId = current.Checkout.RetailerId, ReceivedAmount = current.Checkout.Amount,
                UnappliedCreditAmount = 0, Status = PaymentReceiptStatus.Confirmed,
                Reference = verified.Data.Reference, Note = "PayOS xác nhận thanh toán công nợ gộp",
                ConfirmedAt = paidAt, OperationKey = $"payos-debt:{current.Checkout.Id:N}",
                Version = 1, CreatedAt = now, UpdatedAt = now
            };
            store.AddReceipt(receipt, current.Allocations.Select(x => new PaymentReceiptAllocation
            {
                Id = Guid.NewGuid(), PaymentReceiptId = receipt.Id, PaymentId = x.PaymentId,
                Amount = x.Amount, CreatedAt = now
            }));
            foreach (var allocation in current.Allocations)
            {
                var payment = current.Payments.Single(x => x.Payment.Id == allocation.PaymentId).Payment;
                payment.ConfirmedAmount += allocation.Amount;
                payment.Status = payment.ConfirmedAmount >= payment.AdjustedAmount
                    ? PaymentStatuses.Paid : PaymentStatuses.PartiallyPaid;
                payment.PaidDate = payment.Status == PaymentStatuses.Paid
                    ? DateOnly.FromDateTime(paidAt.ToOffset(VietnamOffset).DateTime) : null;
                payment.CollectionMethod = PaymentCollectionMethods.BankTransfer;
                payment.Version++;
                payment.UpdatedAt = now;
            }
            affectedOrderIds = current.Payments.Select(x => x.Order.Id).Distinct().ToArray();
            current.Checkout.Status = RetailerDebtPayOsStatuses.Paid;
            current.Checkout.PayOsReference = verified.Data.Reference;
            current.Checkout.WebhookHash = hash;
            current.Checkout.PaymentReceiptId = receipt.Id;
            current.Checkout.ConfirmedAt = paidAt;
            current.Checkout.Version++;
            current.Checkout.UpdatedAt = now;
            receiptId = receipt.Id;
            store.AddAuditLog(new AuditLog
            {
                Id = Guid.NewGuid(), EntityName = nameof(RetailerDebtPayOsCheckout),
                EntityId = current.Checkout.Id, Action = "Paid",
                NewValues = JsonSerializer.Serialize(new { ReceiptId = receipt.Id, current.Checkout.Amount }),
                Reason = receipt.Note, CreatedAt = now
            });
            await store.SaveChangesAsync(token);
        }, cancellationToken);
        store.ClearTracking();
        if (completionService is not null)
            foreach (var orderId in affectedOrderIds)
                await completionService.EvaluateAsync(orderId,
                    existing.Retailer.UserAccountId ?? Guid.Empty, cancellationToken);
        return new(true, "Debt checkout was confirmed by payOS.", receiptId);
    }

    private async Task MarkExpiredAsync(long orderCode, CancellationToken cancellationToken)
    {
        await store.ExecuteInTransactionAsync(async token =>
        {
            var current = await store.FindByPayOsOrderCodeAsync(orderCode, true, token);
            if (current?.Checkout.Status != RetailerDebtPayOsStatuses.Pending || !IsExpired(current.Checkout)) return;
            current.Checkout.Status = RetailerDebtPayOsStatuses.Expired;
            current.Checkout.Version++;
            current.Checkout.UpdatedAt = timeProvider.GetUtcNow();
            await store.SaveChangesAsync(token);
        }, cancellationToken);
        store.ClearTracking();
    }

    private async Task ReconcilePendingCheckoutAsync(
        RetailerDebtPayOsCheckout checkout, CancellationToken cancellationToken)
    {
        string? resolvedStatus = null;
        try
        {
            var link = await payOsGateway.GetPaymentLinkStatusAsync(
                checkout.PayOsOrderCode, cancellationToken);
            if (link.OrderCode != checkout.PayOsOrderCode ||
                !string.Equals(link.PaymentLinkId, checkout.PaymentLinkId, StringComparison.Ordinal))
                throw PaymentOperationException.Gateway("payOS returned a different payment link.");

            resolvedStatus = link.Status.ToUpperInvariant() switch
            {
                "CANCELLED" => RetailerDebtPayOsStatuses.Cancelled,
                "EXPIRED" => RetailerDebtPayOsStatuses.Expired,
                _ when IsExpired(checkout) => RetailerDebtPayOsStatuses.Expired,
                _ => null
            };
        }
        catch (PaymentOperationException)
        {
            // A temporary PayOS outage must not make the retailer's existing
            // checkout unreadable. The next mobile poll retries reconciliation.
            return;
        }

        if (resolvedStatus is null) return;
        await store.ExecuteInTransactionAsync(async token =>
        {
            var current = await store.FindByPayOsOrderCodeAsync(checkout.PayOsOrderCode, true, token);
            if (current?.Checkout.Status != RetailerDebtPayOsStatuses.Pending) return;
            current.Checkout.Status = resolvedStatus;
            current.Checkout.Version++;
            current.Checkout.UpdatedAt = timeProvider.GetUtcNow();
            store.AddAuditLog(new AuditLog
            {
                Id = Guid.NewGuid(), EntityName = nameof(RetailerDebtPayOsCheckout),
                EntityId = current.Checkout.Id, Action = resolvedStatus,
                NewValues = JsonSerializer.Serialize(new { Source = "PayOS payment-link reconciliation" }),
                Reason = "Đồng bộ trạng thái link thanh toán PayOS", CreatedAt = current.Checkout.UpdatedAt
            });
            await store.SaveChangesAsync(token);
        }, cancellationToken);
        store.ClearTracking();
    }

    private static RetailerDebtPayOsCheckoutResult ToResult(RetailerDebtCheckoutView view) => new(
        view.Checkout.Id, view.Checkout.Status, view.Checkout.Amount, view.Checkout.PayOsOrderCode,
        view.Checkout.PaymentLinkId, view.Checkout.CheckoutUrl, view.Checkout.QrCode,
        view.Checkout.PayOsExpiredAt, view.Checkout.PayOsReference, view.Checkout.PaymentReceiptId,
        view.Checkout.Version, view.Checkout.CreatedAt, view.Checkout.UpdatedAt,
        view.Allocations.Select(a =>
        {
            var payment = view.Payments.Single(x => x.Payment.Id == a.PaymentId);
            return new RetailerDebtCheckoutAllocationResult(a.PaymentId, payment.Order.Id, payment.Order.OrderCode, a.Amount);
        }).OrderBy(x => x.OrderCode, StringComparer.Ordinal).ToArray());

    private bool IsExpired(RetailerDebtPayOsCheckout checkout) => checkout.PayOsExpiredAt.HasValue &&
        timeProvider.GetUtcNow().ToUnixTimeSeconds() >= checkout.PayOsExpiredAt.Value;
    private static string NormalizeKey(string value) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > 80
        ? throw PaymentOperationException.Validation("Idempotency-Key is required and must not exceed 80 characters.") : value.Trim();
    private static string NormalizeMode(string value) => value?.Trim() switch
    {
        RetailerDebtCheckoutModes.Selected => RetailerDebtCheckoutModes.Selected,
        RetailerDebtCheckoutModes.All => RetailerDebtCheckoutModes.All,
        _ => throw PaymentOperationException.Validation("Checkout mode must be Selected or All.")
    };
    private static Guid[] NormalizeIds(string mode, IReadOnlyCollection<Guid> values)
    {
        var ids = values.Where(x => x != Guid.Empty).Distinct().Order().ToArray();
        if (mode == RetailerDebtCheckoutModes.Selected && (ids.Length == 0 || ids.Length != values.Count))
            throw PaymentOperationException.Validation("Selected checkout requires unique payment identifiers.");
        if (mode == RetailerDebtCheckoutModes.All && values.Count != 0)
            throw PaymentOperationException.Validation("All checkout must not include payment identifiers.");
        return ids;
    }
    private static string HashRequest(string mode, IEnumerable<Guid> ids) => Hash($"{mode}|{string.Join(',', ids.Select(x => x.ToString("N")))}");
    private static string HashWebhook(PayOsWebhookPayload data) => Hash($"{data.OrderCode}|{data.Amount}|{data.PaymentLinkId}|{data.Reference}");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static long CreatePayOsOrderCode(Guid id) { var value = Convert.ToInt64(id.ToString("N")[..13], 16); return value == 0 ? 1 : value; }
    private static long ToPayOsAmount(decimal amount) => amount <= 0 || amount != decimal.Truncate(amount) || amount > long.MaxValue
        ? throw PaymentOperationException.Validation("payOS requires a positive whole-number VND amount.") : decimal.ToInt64(amount);
    private static void ValidateWebhook(RetailerDebtCheckoutView view, PayOsWebhookPayload data)
    {
        if (view.Checkout.PaymentLinkId != data.PaymentLinkId) throw PaymentOperationException.Conflict("payOS payment-link identifier does not match.");
        if (ToPayOsAmount(view.Checkout.Amount) != data.Amount) throw PaymentOperationException.Conflict("payOS debt payment amount does not match.");
        if (!string.Equals(data.Currency, "VND", StringComparison.OrdinalIgnoreCase)) throw PaymentOperationException.Conflict("payOS payment currency must be VND.");
        if (string.IsNullOrWhiteSpace(data.Reference) || data.Reference.Length > 74) throw PaymentOperationException.Validation("payOS transaction reference is invalid.");
    }
    private static DateTimeOffset ParseTransactionTime(string value, DateTimeOffset fallback) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), VietnamOffset) : fallback;
}
