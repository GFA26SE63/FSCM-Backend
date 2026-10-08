using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.DbContexts;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
