using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Envoy.Core.Data;

public class EnvoyDbContextFactory : IDesignTimeDbContextFactory<EnvoyDbContext>
{
    public EnvoyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EnvoyDbContext>();
        optionsBuilder.UseSqlite("Data Source=envoy_design.db");
        return new EnvoyDbContext(optionsBuilder.Options);
    }
}
