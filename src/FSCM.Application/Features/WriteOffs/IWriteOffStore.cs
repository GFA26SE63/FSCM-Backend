using FSCM.Domain.Entities;

namespace FSCM.Application.Features.WriteOffs;

public interface IWriteOffStore
{
    Task<WriteOff?> FindAsync(Guid writeOffId, CancellationToken cancellationToken);
    Task AddAttachmentAsync(WriteOffAttachment attachment, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<WriteOffAttachment>> ListAttachmentsAsync(Guid writeOffId, CancellationToken cancellationToken);
    Task<WriteOffAttachment?> FindAttachmentAsync(Guid writeOffId, Guid attachmentId, CancellationToken cancellationToken);
    Task RemoveAttachmentAsync(WriteOffAttachment attachment, CancellationToken cancellationToken);
}
