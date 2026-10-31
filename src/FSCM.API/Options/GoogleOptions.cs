namespace FSCM.API.Options;

public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    public string[] ClientIds { get; set; } = [];
}
