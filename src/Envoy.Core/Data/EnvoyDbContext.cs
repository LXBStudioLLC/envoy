using Envoy.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Envoy.Core.Data;

public class EnvoyDbContext : DbContext
{
    public DbSet<MasterProfile> MasterProfiles { get; set; } = null!;
    public DbSet<TailoredProfile> TailoredProfiles { get; set; } = null!;
    public DbSet<ApplicationLog> ApplicationLogs { get; set; } = null!;

    public string DbPath { get; }

    public EnvoyDbContext()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Envoy");
        Directory.CreateDirectory(folder);
        DbPath = Path.Combine(folder, "envoy.db");
    }

    public EnvoyDbContext(DbContextOptions<EnvoyDbContext> options) : base(options)
    {
        DbPath = "";
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Envoy");
            Directory.CreateDirectory(folder);
            var dbPath = Path.Combine(folder, "envoy.db");
            options.UseSqlite($"Data Source={dbPath}");
        }
    }

    // EF Core never passes null to these for entities with non-nullable List<T>
    // properties. The null-forgiving operators silence false-positive nullability
    // warnings from the compiler's expression-tree analysis.
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (c1, c2) => c1!.SequenceEqual(c2!),
        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
        c => c.ToList());

    private static readonly ValueComparer<List<ParseAnomaly>> AnomalyListComparer = new(
        (c1, c2) => c1!.SequenceEqual(c2!),
        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.Field.GetHashCode(), v.Message.GetHashCode(), v.Severity.GetHashCode())),
        c => c.ToList());

    private static readonly ValueComparer<List<string>> NewlineListComparer = new(
        (c1, c2) => c1!.SequenceEqual(c2!),
        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
        c => c.ToList());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MasterProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Email).IsRequired();

            entity.OwnsMany(e => e.Experience, exp =>
            {
                exp.WithOwner().HasForeignKey("MasterProfileId");
                exp.Property(e => e.Bullets).HasConversion(
                    v => string.Join("\n", v),
                    v => v.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    StringListComparer);
            });

            entity.OwnsMany(e => e.Education, edu =>
            {
                edu.WithOwner().HasForeignKey("MasterProfileId");
            });

            entity.OwnsMany(e => e.Projects, proj =>
            {
                proj.WithOwner().HasForeignKey("MasterProfileId");
                proj.Property(e => e.Technologies).HasConversion(
                    v => string.Join(",", v),
                    v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    StringListComparer);
            });

            entity.Property(e => e.Skills).HasConversion(
                v => string.Join(",", v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                StringListComparer);

            entity.Property(e => e.Anomalies).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => DeserializeJsonOrEmpty<List<ParseAnomaly>>(v, "Anomalies") ?? new List<ParseAnomaly>(),
                AnomalyListComparer);
        });

        modelBuilder.Entity<TailoredProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.JobUrl).IsRequired();

            entity.Property(e => e.TailoredData).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => DeserializeJsonOrEmpty<MasterProfile>(v, "TailoredData") ?? new MasterProfile());

            entity.Property(e => e.SafetyResult).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => DeserializeJsonOrEmpty<SafetyResult>(v, "SafetyResult") ?? new SafetyResult());

            entity.Property(e => e.ChangesMade).HasConversion(
                v => string.Join("\n", v),
                v => v.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(),
                NewlineListComparer);
        });

        modelBuilder.Entity<ApplicationLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.JobUrl).IsRequired();
        });
    }

    // Helper used by ValueConverter expressions on JSON columns. If a row
    // contains corrupted JSON we don't want to throw and break the entire
    // load — but we also don't want to silently lose the user's data with
    // no signal. Log the failure (to Debug — the DbContext has no ILogger
    // in scope here) and return null so the caller's `?? new()` fallback
    // produces an empty-but-valid value for the property.
    private static T? DeserializeJsonOrEmpty<T>(string json, string columnName) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, (System.Text.Json.JsonSerializerOptions?)null);
        }
        catch (System.Text.Json.JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[EnvoyDbContext] Failed to deserialize {columnName} as {typeof(T).Name}: {ex.Message}. " +
                $"Length={json.Length}, preview={json[..Math.Min(120, json.Length)]}");
            return null;
        }
    }
}