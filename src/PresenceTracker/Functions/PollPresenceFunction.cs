using System.Collections.Concurrent;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PresenceTracker.Models;
using PresenceTracker.Services;

namespace PresenceTracker.Functions;

public class PollPresenceFunction
{
    private readonly GraphPresenceService _graphService;
    private readonly PresenceStorageService _storageService;
    private readonly GroupMemberCache _memberCache;
    private readonly ILogger<PollPresenceFunction> _logger;

    // In-memory cache of last-known presence per user
    private static readonly ConcurrentDictionary<string, (string Availability, string Activity)> _lastKnownStates = new();
    private static DateTime _lastSuccessfulPollUtc = DateTime.MinValue;
    private static bool _initialized = false;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly TimeSpan GapThreshold = TimeSpan.FromMinutes(2);

    public PollPresenceFunction(
        GraphPresenceService graphService,
        PresenceStorageService storageService,
        GroupMemberCache memberCache,
        ILogger<PollPresenceFunction> logger)
    {
        _graphService = graphService;
        _storageService = storageService;
        _memberCache = memberCache;
        _logger = logger;
    }

    [Function("PollPresence")]
    public async Task Run([TimerTrigger("0 */1 * * * *")] TimerInfo timerInfo)
    {
        var pollTimeUtc = DateTime.UtcNow;

        try
        {
            // Cold start: load last-known states from DB
            await EnsureInitializedAsync();

            // Get tracked group members (cached, refreshed every 10 min)
            var members = await _memberCache.GetMembersAsync(_graphService);
            if (members.Count == 0)
            {
                _logger.LogWarning("No members found in security group. Skipping poll.");
                return;
            }

            // Build lookup for user metadata
            var userLookup = members.ToDictionary(m => m.Id, m => m);

            // Poll Graph for current presence
            var userIds = members.Select(m => m.Id).ToList();
            var presences = await _graphService.GetPresencesAsync(userIds);

            // Detect gap (function was down for >2 min)
            bool hasGap = _lastSuccessfulPollUtc != DateTime.MinValue
                          && (pollTimeUtc - _lastSuccessfulPollUtc) > GapThreshold;

            var changesToInsert = new List<PresenceChange>();

            foreach (var presence in presences)
            {
                if (presence.Id == null) continue;

                var userId = presence.Id;
                var availability = presence.Availability?.ToString() ?? "PresenceUnknown";
                var activity = presence.Activity?.ToString() ?? "PresenceUnknown";

                userLookup.TryGetValue(userId, out var user);

                // If there was a gap, insert an "Unknown" marker at the gap start time
                if (hasGap && _lastKnownStates.ContainsKey(userId))
                {
                    changesToInsert.Add(new PresenceChange
                    {
                        UserId = userId,
                        UserDisplayName = user?.DisplayName,
                        UserPrincipalName = user?.UserPrincipalName,
                        Availability = "Unknown",
                        Activity = "Unknown",
                        DetectedAtUtc = _lastSuccessfulPollUtc.AddMinutes(1)
                    });
                }

                // Check if status changed from last known
                bool isNew = !_lastKnownStates.TryGetValue(userId, out var lastState);
                bool changed = isNew || lastState.Availability != availability || lastState.Activity != activity;

                if (changed)
                {
                    changesToInsert.Add(new PresenceChange
                    {
                        UserId = userId,
                        UserDisplayName = user?.DisplayName,
                        UserPrincipalName = user?.UserPrincipalName,
                        Availability = availability,
                        Activity = activity,
                        DetectedAtUtc = pollTimeUtc
                    });

                    _lastKnownStates[userId] = (availability, activity);
                }
            }

            // Persist changes
            if (changesToInsert.Count > 0)
            {
                await _storageService.SaveChangesAsync(changesToInsert);
                _logger.LogInformation("Saved {Count} presence changes at {Time}.",
                    changesToInsert.Count, pollTimeUtc);
            }
            else
            {
                _logger.LogDebug("No presence changes detected at {Time}.", pollTimeUtc);
            }

            _lastSuccessfulPollUtc = pollTimeUtc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling presence at {Time}. Will retry next cycle.", pollTimeUtc);
            // Don't rethrow — let the next timer invocation try again
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            _logger.LogInformation("Cold start detected. Loading last-known states from database...");
            var states = await _storageService.GetLastKnownStatesAsync();

            foreach (var (userId, state) in states)
            {
                _lastKnownStates[userId] = state;
            }

            _logger.LogInformation("Loaded {Count} last-known states from database.", states.Count);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
