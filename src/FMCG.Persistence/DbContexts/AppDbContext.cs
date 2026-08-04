using Microsoft.EntityFrameworkCore;

namespace FMCG.Persistence.DbContexts;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
