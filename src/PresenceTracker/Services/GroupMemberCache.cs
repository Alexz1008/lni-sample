using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PresenceTracker.Services;

public class GroupMemberCache
{
    private readonly ILogger<GroupMemberCache> _logger;
    private List<TrackedUser> _cachedMembers = [];
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public GroupMemberCache(ILogger<GroupMemberCache> logger)
    {
        _logger = logger;
    }

    public async Task<List<TrackedUser>> GetMembersAsync(GraphPresenceService graphService)
    {
        if (DateTime.UtcNow - _lastRefresh < CacheDuration && _cachedMembers.Count > 0)
        {
            return _cachedMembers;
        }

        await _refreshLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (DateTime.UtcNow - _lastRefresh < CacheDuration && _cachedMembers.Count > 0)
            {
                return _cachedMembers;
            }

            _logger.LogInformation("Refreshing group member cache...");
            _cachedMembers = await graphService.GetGroupMembersAsync();
            _lastRefresh = DateTime.UtcNow;
            _logger.LogInformation("Group member cache refreshed with {Count} members.", _cachedMembers.Count);
        }
        finally
        {
            _refreshLock.Release();
        }

        return _cachedMembers;
    }
}
