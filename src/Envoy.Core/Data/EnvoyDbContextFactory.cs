using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Envoy.Core.Data;

// Used by `dotnet ef migrations add` at design time. The connection string
// only matters for the EF tooling to instantiate the context — the schema
// it generates comes from the model classes, not from any data in the DB.
// We point at the same %LOCALAPPDATA%\Envoy\envoy.db that the runtime uses
// so `dotnet ef database update` against a developer's machine doesn't
// create a stray envoy_design.db alongside the real one.
public class EnvoyDbContextFactory : IDesignTimeDbContextFactory<EnvoyDbContext>
{
    public EnvoyDbContext CreateDbContext(string[] args)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Envoy");
        Directory.CreateDirectory(folder);
        var dbPath = Path.Combine(folder, "envoy.db");

        var optionsBuilder = new DbContextOptionsBuilder<EnvoyDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
        return new EnvoyDbContext(optionsBuilder.Options);
    }
}
