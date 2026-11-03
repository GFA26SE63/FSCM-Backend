using FSCM.Application.Features.Storage;
using FSCM.Application.Features.WriteOffs;
using FSCM.Domain.Entities;
using FSCM.UnitTests.Storage;

namespace FSCM.UnitTests.WriteOffs;

public sealed class WriteOffServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Add_attachment_stores_image_and_links_to_write_off()
    {
        var fixture = new Fixture();
        var writeOff = fixture.AddWriteOff();

        var result = await fixture.Service.AddAttachmentAsync(
            writeOff.Id, fixture.ActorId, FakeImageFactory.Jpeg(), CancellationToken.None);

        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Contains("WriteOffAttachment", result.Url);
        var stored = Assert.Single(fixture.Store.Attachments);
        Assert.Equal(writeOff.Id, stored.WriteOffId);
        Assert.Single(fixture.Images.Blobs);
    }

    [Fact]
    public async Task Add_attachment_rejects_unknown_write_off()
    {
        var fixture = new Fixture();

        var error = await Assert.ThrowsAsync<WriteOffOperationException>(() =>
            fixture.Service.AddAttachmentAsync(Guid.NewGuid(), fixture.ActorId, FakeImageFactory.Png(), CancellationToken.None));

        Assert.Equal("not_found", error.Code);
    }

    [Fact]
    public async Task Remove_attachment_deletes_blob()
    {
        var fixture = new Fixture();
        var writeOff = fixture.AddWriteOff();
        var added = await fixture.Service.AddAttachmentAsync(
            writeOff.Id, fixture.ActorId, FakeImageFactory.Png(), CancellationToken.None);

        await fixture.Service.RemoveAttachmentAsync(writeOff.Id, added.Id, CancellationToken.None);

        Assert.Empty(fixture.Store.Attachments);
        Assert.Single(fixture.Images.Deleted);
    }

    private sealed class Fixture
    {
        public Guid ActorId { get; } = Guid.NewGuid();
        public FakeWriteOffStore Store { get; } = new();
        public FakeImageStorage Images { get; } = new();
        public WriteOffService Service { get; }

        public Fixture() =>
            Service = new WriteOffService(Store, Images.ToUploadService(), new FixedTimeProvider(Now));

        public WriteOff AddWriteOff()
        {
            var writeOff = new WriteOff { Id = Guid.NewGuid(), WriteOffCode = "WO-0001", Status = "Draft" };
            Store.WriteOffs.Add(writeOff);
            return writeOff;
        }
    }

    private sealed class FakeWriteOffStore : IWriteOffStore
    {
        public List<WriteOff> WriteOffs { get; } = [];
        public List<WriteOffAttachment> Attachments { get; } = [];

        public Task<WriteOff?> FindAsync(Guid writeOffId, CancellationToken ct) =>
            Task.FromResult(WriteOffs.SingleOrDefault(x => x.Id == writeOffId));
        public Task AddAttachmentAsync(WriteOffAttachment attachment, CancellationToken ct)
        {
            Attachments.Add(attachment);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyCollection<WriteOffAttachment>> ListAttachmentsAsync(Guid writeOffId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<WriteOffAttachment>>(
                Attachments.Where(x => x.WriteOffId == writeOffId).ToArray());
        public Task<WriteOffAttachment?> FindAttachmentAsync(Guid writeOffId, Guid attachmentId, CancellationToken ct) =>
            Task.FromResult(Attachments.SingleOrDefault(x => x.Id == attachmentId && x.WriteOffId == writeOffId));
        public Task RemoveAttachmentAsync(WriteOffAttachment attachment, CancellationToken ct)
        {
            Attachments.Remove(attachment);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
