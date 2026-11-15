using FSCM.Application.Features.Complaints;

namespace FSCM.UnitTests.Complaints;

public sealed class RequestSettlementTests
{
    [Theory]
    [InlineData(500000,1000000,600000,0,400000,100000)]
    [InlineData(100000,1000000,0,0,100000,0)]
    [InlineData(100000,1000000,1000000,0,0,100000)]
    [InlineData(100000,900000,1000000,100000,0,100000)]
    [InlineData(100000,900000,1000000,0,0,100000)]
    public void Credit_is_offset_before_only_the_new_paid_surplus_is_refunded(decimal amount,decimal owed,decimal paid,decimal refunded,decimal expectedOffset,decimal expectedRefund)
    {
        var split = RequestSettlement.Calculate(amount,owed,paid,refunded);
        Assert.Equal(expectedOffset,split.DebtOffset); Assert.Equal(expectedRefund,split.Refund);
        Assert.Equal(amount, split.DebtOffset + split.Refund);
    }
}
