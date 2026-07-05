using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Envoy.Core.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EnvoyDbContext>>();
        using var context = factory.CreateDbContext();

        try
        {
            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Envoy could not initialize its local database. Common causes:\n" +
                "  - Another instance of Envoy is already running (close it and try again).\n" +
                "  - Antivirus or OneDrive is locking %LOCALAPPDATA%\\Envoy\\envoy.db.\n" +
                "  - %LOCALAPPDATA% is read-only or full.\n" +
                $"Underlying error: {ex.Message}",
                ex);
        }

        // Switch SQLite into WAL mode so readers don't block writers and vice-versa.
        // synchronous=NORMAL keeps durability sane for desktop usage while avoiding
        // the fsync-per-write cost of FULL.
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;");
        }
        catch
        {
            // PRAGMA failures aren't fatal — the DB still works in default mode.
        }

        CleanupOldArtifacts();
    }

    // Tailored-resume PDFs are written to %LOCALAPPDATA%/Envoy/ on every
    // application attempt. Failed runs leave their PDFs behind. Sweep any
    // older than 30 days on startup so the folder doesn't grow forever.
    // The user can always re-generate from the Vault.
    private static void CleanupOldArtifacts()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Envoy");
            if (!Directory.Exists(dir)) return;

            var cutoff = DateTime.UtcNow - TimeSpan.FromDays(30);
            foreach (var pdf in Directory.GetFiles(dir, "*.pdf"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(pdf) < cutoff)
                        File.Delete(pdf);
                }
                catch
                {
                    // Best-effort cleanup; if a PDF is locked or unreadable, skip.
                }
            }
        }
        catch
        {
            // Housekeeping must never break startup.
        }
    }
}
