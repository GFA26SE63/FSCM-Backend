namespace FSCM.API.Options;

public sealed class EntraOptions
{
    public const string SectionName = "Entra";

    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;
}
