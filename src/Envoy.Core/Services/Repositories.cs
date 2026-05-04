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
    private readonly EnvoyDbContext _db;
    private readonly ILogger<ProfileRepository> _log;

    public ProfileRepository(EnvoyDbContext db, ILogger<ProfileRepository> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<List<MasterProfile>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.MasterProfiles
            .AsNoTracking()
            .Include(p => p.Experience)
            .Include(p => p.Education)
            .Include(p => p.Projects)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<MasterProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.MasterProfiles
            .AsNoTracking()
            .Include(p => p.Experience)
            .Include(p => p.Education)
            .Include(p => p.Projects)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task AddAsync(MasterProfile profile, CancellationToken ct = default)
    {
        _db.MasterProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(MasterProfile profile, CancellationToken ct = default)
    {
        var tracked = await _db.MasterProfiles.FindAsync(new object[] { profile.Id }, ct);
        if (tracked == null) return;

        _db.Entry(tracked).CurrentValues.SetValues(profile);
        tracked.Skills = profile.Skills;
        tracked.Anomalies = profile.Anomalies;

        _db.Entry(tracked).Collection(p => p.Experience).Load();
        _db.Entry(tracked).Collection(p => p.Education).Load();
        _db.Entry(tracked).Collection(p => p.Projects).Load();

        ReplaceOwnedCollection(tracked.Experience, profile.Experience, _db);
        ReplaceOwnedCollection(tracked.Education, profile.Education, _db);
        ReplaceOwnedCollection(tracked.Projects, profile.Projects, _db);

        await _db.SaveChangesAsync(ct);
    }

    private static void ReplaceOwnedCollection<T>(ICollection<T> tracked, ICollection<T> updated, EnvoyDbContext db)
    {
        tracked.Clear();
        foreach (var item in updated)
            tracked.Add(item);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var profile = await _db.MasterProfiles.FindAsync(new object[] { id }, ct);
        if (profile != null)
        {
            _db.MasterProfiles.Remove(profile);
            await _db.SaveChangesAsync(ct);
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
    private readonly EnvoyDbContext _db;
    private readonly ILogger<TailoredProfileRepository> _log;

    public TailoredProfileRepository(EnvoyDbContext db, ILogger<TailoredProfileRepository> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<TailoredProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.TailoredProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<List<TailoredProfile>> GetByMasterProfileIdAsync(Guid masterProfileId, CancellationToken ct = default)
    {
        return await _db.TailoredProfiles
            .AsNoTracking()
            .Where(p => p.MasterProfileId == masterProfileId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<TailoredProfile>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.TailoredProfiles
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(TailoredProfile profile, CancellationToken ct = default)
    {
        _db.TailoredProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TailoredProfile profile, CancellationToken ct = default)
    {
        _db.TailoredProfiles.Update(profile);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var profile = await _db.TailoredProfiles.FindAsync(new object[] { id }, ct);
        if (profile != null)
        {
            _db.TailoredProfiles.Remove(profile);
            await _db.SaveChangesAsync(ct);
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
    private readonly EnvoyDbContext _db;
    private readonly ILogger<ApplicationLogRepository> _log;

    public ApplicationLogRepository(EnvoyDbContext db, ILogger<ApplicationLogRepository> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<List<ApplicationLog>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.ApplicationLogs
            .AsNoTracking()
            .OrderByDescending(l => l.StartedAt)
            .ToListAsync(ct);
    }

    public async Task<ApplicationLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.ApplicationLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task AddAsync(ApplicationLog log, CancellationToken ct = default)
    {
        _db.ApplicationLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ApplicationLog log, CancellationToken ct = default)
    {
        _db.ApplicationLogs.Update(log);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ApplicationLog?> GetByTailoredProfileIdAsync(Guid tailoredProfileId, CancellationToken ct = default)
    {
        return await _db.ApplicationLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.TailoredProfileId == tailoredProfileId, ct);
    }
}
