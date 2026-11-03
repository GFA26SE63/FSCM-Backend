using FSCM.Application.Features.WriteOffs;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.WriteOffs;

public sealed class WriteOffStore(AppDbContext db) : IWriteOffStore
{
    public Task<WriteOff?> FindAsync(Guid writeOffId, CancellationToken cancellationToken) =>
        db.WriteOffRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Id == writeOffId, cancellationToken);

    public async Task AddAttachmentAsync(WriteOffAttachment attachment, CancellationToken cancellationToken)
    {
        db.WriteOffAttachmentRecords.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<WriteOffAttachment>> ListAttachmentsAsync(
        Guid writeOffId, CancellationToken cancellationToken) =>
        await db.WriteOffAttachmentRecords.AsNoTracking()
            .Where(x => x.WriteOffId == writeOffId)
            .OrderBy(x => x.UploadedAt)
            .ToArrayAsync(cancellationToken);

    public Task<WriteOffAttachment?> FindAttachmentAsync(
        Guid writeOffId, Guid attachmentId, CancellationToken cancellationToken) =>
        db.WriteOffAttachmentRecords
            .SingleOrDefaultAsync(x => x.Id == attachmentId && x.WriteOffId == writeOffId, cancellationToken);

    public async Task RemoveAttachmentAsync(WriteOffAttachment attachment, CancellationToken cancellationToken)
    {
        db.WriteOffAttachmentRecords.Remove(attachment);
        await db.SaveChangesAsync(cancellationToken);
    }
}
