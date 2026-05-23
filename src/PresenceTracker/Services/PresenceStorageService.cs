using Microsoft.EntityFrameworkCore;
using PresenceTracker.Data;
using PresenceTracker.Models;

namespace PresenceTracker.Services;

public class PresenceStorageService
{
    private readonly IDbContextFactory<PresenceDbContext> _dbContextFactory;

    public PresenceStorageService(IDbContextFactory<PresenceDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<Dictionary<string, (string Availability, string Activity)>> GetLastKnownStatesAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        // Get the most recent row per user using a subquery on max Id
        var lastStates = await db.PresenceChanges
            .FromSqlRaw("""
                SELECT p.*
                FROM PresenceChanges p
                INNER JOIN (
                    SELECT UserId, MAX(Id) AS MaxId
                    FROM PresenceChanges
                    GROUP BY UserId
                ) latest ON p.Id = latest.MaxId
                """)
            .AsNoTracking()
            .ToListAsync();

        return lastStates.ToDictionary(
            s => s.UserId,
            s => (s.Availability, s.Activity));
    }

    public async Task SaveChangesAsync(IReadOnlyList<PresenceChange> changes)
    {
        if (changes.Count == 0) return;

        await using var db = await _dbContextFactory.CreateDbContextAsync();
        db.PresenceChanges.AddRange(changes);
        await db.SaveChangesAsync();
    }
}
