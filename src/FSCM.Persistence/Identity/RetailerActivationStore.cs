using FSCM.Application.Features.Identity;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Identity;

public sealed class RetailerActivationStore(AppDbContext dbContext) : IRetailerActivationStore
{
    public Task<UserAccount?> FindUserByPhoneAsync(string phone, CancellationToken token) =>
        dbContext.UserAccountRecords.SingleOrDefaultAsync(item => item.Phone == phone, token);
    public Task<UserAccount?> FindUserByIdAsync(Guid id, CancellationToken token) =>
        dbContext.UserAccountRecords.SingleOrDefaultAsync(item => item.Id == id, token);
    public Task<UserCredential?> FindCredentialAsync(Guid id, CancellationToken token) =>
        dbContext.UserCredentials.SingleOrDefaultAsync(item => item.UserAccountId == id, token);
    public Task<Retailer?> FindRetailerByUserIdAsync(Guid id, CancellationToken token) =>
        dbContext.RetailerRecords.SingleOrDefaultAsync(item => item.UserAccountId == id, token);
    public Task<RetailerActivationChallenge?> FindLatestAsync(Guid id, CancellationToken token) =>
        dbContext.RetailerActivationChallenges.Where(item => item.UserAccountId == id)
            .OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(token);
    public Task<RetailerActivationChallenge?> FindBySetupTokenHashAsync(string hash, CancellationToken token) =>
        dbContext.RetailerActivationChallenges.SingleOrDefaultAsync(item => item.SetupTokenHash == hash, token);
    public Task<int> CountRequestsForAccountAsync(Guid id, DateTimeOffset since, CancellationToken token) =>
        dbContext.RetailerActivationChallenges.CountAsync(item => item.UserAccountId == id && item.CreatedAt >= since, token);
    public Task<int> CountRequestsForIpAsync(string ip, DateTimeOffset since, CancellationToken token) =>
        dbContext.RetailerActivationChallenges.CountAsync(item => item.RequestedIpAddress == ip && item.CreatedAt >= since, token);
    public void AddChallenge(RetailerActivationChallenge challenge) => dbContext.RetailerActivationChallenges.Add(challenge);
    public void AddCredential(UserCredential credential) => dbContext.UserCredentials.Add(credential);
    public Task SaveChangesAsync(CancellationToken token) => dbContext.SaveChangesAsync(token);
}
