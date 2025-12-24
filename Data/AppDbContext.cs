using Microsoft.EntityFrameworkCore;
using SoftcodeUnicontaMiddleware.Data.Entities;

namespace SoftcodeUnicontaMiddleware.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<ApiTenant> Tenants => Set<ApiTenant>();
        public DbSet<ApiClient> Clients => Set<ApiClient>();
    }
}
