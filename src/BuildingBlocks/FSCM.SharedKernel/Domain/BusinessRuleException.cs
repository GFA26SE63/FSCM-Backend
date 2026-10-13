namespace FSCM.SharedKernel.Domain;

/// <summary>Thrown when a business rule is broken (e.g. stock would go negative). The API maps it to HTTP 422.</summary>
public sealed class BusinessRuleException : Exception
{
    public string Code { get; }
    public BusinessRuleException(string code, string message) : base(message) => Code = code;
}

public static class Guard
{
    public static void Against(bool broken, string code, string message)
    {
        if (broken) throw new BusinessRuleException(code, message);
    }
}
