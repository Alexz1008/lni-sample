using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Communications.GetPresencesByUserId;
using Microsoft.Graph.Models;

namespace PresenceTracker.Services;

public record TrackedUser(string Id, string? DisplayName, string? UserPrincipalName);

public class GraphPresenceService
{
    private readonly GraphServiceClient _graphClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GraphPresenceService> _logger;

    public GraphPresenceService(
        GraphServiceClient graphClient,
        IConfiguration configuration,
        ILogger<GraphPresenceService> logger)
    {
        _graphClient = graphClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<TrackedUser>> GetGroupMembersAsync()
    {
        var groupId = _configuration["SecurityGroupId"]
            ?? throw new InvalidOperationException("SecurityGroupId is not configured.");

        var members = new List<TrackedUser>();
        var response = await _graphClient.Groups[groupId].Members
            .GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "displayName", "userPrincipalName"];
                config.QueryParameters.Top = 999;
            });

        if (response?.Value == null) return members;

        foreach (var member in response.Value)
        {
            if (member is User user)
            {
                members.Add(new TrackedUser(user.Id!, user.DisplayName, user.UserPrincipalName));
            }
        }

        // Handle pagination
        var pageIterator = Microsoft.Graph.PageIterator<DirectoryObject, DirectoryObjectCollectionResponse>
            .CreatePageIterator(_graphClient, response, item =>
            {
                if (item is User user)
                {
                    members.Add(new TrackedUser(user.Id!, user.DisplayName, user.UserPrincipalName));
                }
                return true;
            });

        await pageIterator.IterateAsync();

        _logger.LogInformation("Retrieved {Count} members from security group {GroupId}", members.Count, groupId);
        return members;
    }

    public async Task<List<Presence>> GetPresencesAsync(IReadOnlyList<string> userIds)
    {
        var allPresences = new List<Presence>();

        // Graph API supports max 650 users per batch call
        const int batchSize = 650;
        for (int i = 0; i < userIds.Count; i += batchSize)
        {
            var batch = userIds.Skip(i).Take(batchSize).ToList();

            try
            {
                var result = await _graphClient.Communications
                    .GetPresencesByUserId
                    .PostAsGetPresencesByUserIdPostResponseAsync(
                        new GetPresencesByUserIdPostRequestBody
                        {
                            Ids = batch
                        });

                if (result?.Value != null)
                {
                    allPresences.AddRange(result.Value);
                }
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == 429)
            {
                // Throttled — log and skip this batch, next poll will retry
                var retryAfter = ex.ResponseHeaders?
                    .TryGetValues("Retry-After", out var values) == true
                    ? values.FirstOrDefault() : "unknown";
                _logger.LogWarning("Graph API throttled (429). Retry-After: {RetryAfter}. Skipping batch.", retryAfter);
            }
        }

        return allPresences;
    }
}
