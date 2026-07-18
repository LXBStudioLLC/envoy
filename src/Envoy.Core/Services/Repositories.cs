using Envoy.Core.Data;
using Envoy.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

public interface IProfileRepository
{
    Task<List<MasterProfile>> GetAllAsync(CancellationToken ct = default);
    Task<MasterProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(MasterProfile profile, CancellationToken ct = default);
    Task UpdateAsync(MasterProfile profile, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class ProfileRepository : IProfileRepository
{
    private readonly IDbContextFactory<EnvoyDbContext> _factory;
    private readonly ILogger<ProfileRepository> _log;

    public ProfileRepository(IDbContextFactory<EnvoyDbContext> factory, ILogger<ProfileRepository> log)
    {
        _factory = factory;
        _log = log;
    }

    public async Task<List<MasterProfile>> GetAllAsync(CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.MasterProfiles
            .AsNoTracking()
            .Include(p => p.Experience)
            .Include(p => p.Education)
            .Include(p => p.Projects)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<MasterProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.MasterProfiles
            .AsNoTracking()
            .Include(p => p.Experience)
            .Include(p => p.Education)
            .Include(p => p.Projects)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task AddAsync(MasterProfile profile, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        db.MasterProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(MasterProfile profile, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        var tracked = await db.MasterProfiles.FindAsync(new object[] { profile.Id }, ct);
        if (tracked == null) return;

        db.Entry(tracked).CurrentValues.SetValues(profile);
        tracked.Skills = profile.Skills;
        tracked.Anomalies = profile.Anomalies;

        await db.Entry(tracked).Collection(p => p.Experience).LoadAsync(ct);
        await db.Entry(tracked).Collection(p => p.Education).LoadAsync(ct);
        await db.Entry(tracked).Collection(p => p.Projects).LoadAsync(ct);

        ReplaceOwnedCollection(tracked.Experience, profile.Experience);
        ReplaceOwnedCollection(tracked.Education, profile.Education);
        ReplaceOwnedCollection(tracked.Projects, profile.Projects);

        await db.SaveChangesAsync(ct);
    }

    private static void ReplaceOwnedCollection<T>(ICollection<T> tracked, ICollection<T> updated)
    {
        tracked.Clear();
        foreach (var item in updated)
            tracked.Add(item);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        var profile = await db.MasterProfiles.FindAsync(new object[] { id }, ct);
        if (profile != null)
        {
            db.MasterProfiles.Remove(profile);
            await db.SaveChangesAsync(ct);
        }
    }
}

public interface ITailoredProfileRepository
{
    Task<TailoredProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<TailoredProfile>> GetByMasterProfileIdAsync(Guid masterProfileId, CancellationToken ct = default);
    Task<List<TailoredProfile>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(TailoredProfile profile, CancellationToken ct = default);
    Task UpdateAsync(TailoredProfile profile, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class TailoredProfileRepository : ITailoredProfileRepository
{
    private readonly IDbContextFactory<EnvoyDbContext> _factory;
    private readonly ILogger<TailoredProfileRepository> _log;

    public TailoredProfileRepository(IDbContextFactory<EnvoyDbContext> factory, ILogger<TailoredProfileRepository> log)
    {
        _factory = factory;
        _log = log;
    }

    public async Task<TailoredProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.TailoredProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<List<TailoredProfile>> GetByMasterProfileIdAsync(Guid masterProfileId, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.TailoredProfiles
            .AsNoTracking()
            .Where(p => p.MasterProfileId == masterProfileId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<TailoredProfile>> GetAllAsync(CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.TailoredProfiles
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(TailoredProfile profile, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        db.TailoredProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TailoredProfile profile, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        profile.UpdatedAt = DateTime.UtcNow;
        db.TailoredProfiles.Update(profile);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        var profile = await db.TailoredProfiles.FindAsync(new object[] { id }, ct);
        if (profile != null)
        {
            db.TailoredProfiles.Remove(profile);
            await db.SaveChangesAsync(ct);
        }
    }
}

public interface IApplicationLogRepository
{
    Task<List<ApplicationLog>> GetAllAsync(CancellationToken ct = default);
    Task<ApplicationLog?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ApplicationLog log, CancellationToken ct = default);
    Task UpdateAsync(ApplicationLog log, CancellationToken ct = default);
    Task<ApplicationLog?> GetByTailoredProfileIdAsync(Guid tailoredProfileId, CancellationToken ct = default);
}

public class ApplicationLogRepository : IApplicationLogRepository
{
    private readonly IDbContextFactory<EnvoyDbContext> _factory;
    private readonly ILogger<ApplicationLogRepository> _log;

    public ApplicationLogRepository(IDbContextFactory<EnvoyDbContext> factory, ILogger<ApplicationLogRepository> log)
    {
        _factory = factory;
        _log = log;
    }

    public async Task<List<ApplicationLog>> GetAllAsync(CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.ApplicationLogs
            .AsNoTracking()
            .OrderByDescending(l => l.StartedAt)
            .ToListAsync(ct);
    }

    public async Task<ApplicationLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.ApplicationLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task AddAsync(ApplicationLog log, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        db.ApplicationLogs.Add(log);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ApplicationLog log, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        db.ApplicationLogs.Update(log);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ApplicationLog?> GetByTailoredProfileIdAsync(Guid tailoredProfileId, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.ApplicationLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.TailoredProfileId == tailoredProfileId, ct);
    }
}

public interface IJobEventRepository
{
    Task AddAsync(JobEvent jobEvent, CancellationToken ct = default);
    Task<List<JobEvent>> GetAllAsync(CancellationToken ct = default);
    Task<int> CountByTypeAsync(JobEventType type, CancellationToken ct = default);
}

public class JobEventRepository : IJobEventRepository
{
    private readonly IDbContextFactory<EnvoyDbContext> _factory;
    private readonly ILogger<JobEventRepository> _log;

    public JobEventRepository(IDbContextFactory<EnvoyDbContext> factory, ILogger<JobEventRepository> log)
    {
        _factory = factory;
        _log = log;
    }

    public async Task AddAsync(JobEvent jobEvent, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        db.JobEvents.Add(jobEvent);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<JobEvent>> GetAllAsync(CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.JobEvents
            .AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(ct);
    }

    public async Task<int> CountByTypeAsync(JobEventType type, CancellationToken ct = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.JobEvents
            .AsNoTracking()
            .CountAsync(e => e.Type == type, ct);
    }
}
