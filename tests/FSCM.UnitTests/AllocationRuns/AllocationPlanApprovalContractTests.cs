using System.Reflection;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Orders;

namespace FSCM.UnitTests.AllocationRuns;

/// <summary>
/// Regression contracts for Report5's retired human allocation-plan workflow.
/// These tests deliberately inspect the public application boundary: a client must not be able to
/// revive the old approval or manual allocation-edit commands through a newly exposed service API.
/// </summary>
public sealed class AllocationPlanApprovalContractTests
{
    [Fact]
    public void UT_Allocation_Plan_Approval_UTCID02_rejects_the_obsolete_human_approval_contract()
    {
        var orderOperations = PublicOperationNames(typeof(OrderService));

        Assert.Contains(nameof(OrderService.RetryAllocationAsync), orderOperations);
        Assert.DoesNotContain(orderOperations, name =>
            name.Contains("approve", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("reject", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UT_Allocation_Plan_Approval_UTCID04_does_not_expose_a_batch_or_score_override_contract()
    {
        var allocationOperations = PublicOperationNames(typeof(IRealTimeAllocationService))
            .Concat(PublicOperationNames(typeof(RealTimeAllocationService)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(IRealTimeAllocationService.AllocateAsync)], allocationOperations);
        Assert.DoesNotContain(allocationOperations, name =>
            name.Contains("batch", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("score", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("override", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("replace", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] PublicOperationNames(Type type) => type
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Select(method => method.Name)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
}
