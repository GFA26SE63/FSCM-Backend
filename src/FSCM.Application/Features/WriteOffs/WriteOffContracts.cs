namespace FSCM.Application.Features.WriteOffs;

/// <summary>One evidence image of a write-off. <see cref="Url"/> is a short-lived read SAS URL.</summary>
public sealed record WriteOffAttachmentResult(
    Guid Id, string FileName, string ContentType, string Url, DateTimeOffset UploadedAt);
