# Teams Presence Tracker

Azure Function that polls Microsoft Teams presence status for users in a security group and stores state changes in Azure SQL Database for Power BI reporting.

## Architecture

```
[Timer: every 1 min] → Azure Function → Microsoft Graph (batch presence poll)
                                       → Compare vs last-known state (in-memory)
                                       → If changed → INSERT into Azure SQL
                                                              ↓
                                                        PresenceChanges table
                                                        ├── vw_PresenceWithDuration
                                                        ├── vw_MonthlyPresenceSummary
                                                        └── vw_DailyPresenceTimeline
                                                              ↓
                                                           Power BI
```

Only status **changes** are stored (not every poll). Each row represents a state transition with `DetectedAtUtc`. Durations are computed via SQL `LEAD()` window function — no data duplication.

## Prerequisites

- Azure subscription
- Azure SQL Database
- Azure AD (Entra ID) app registration
- .NET 8 SDK
- Azure Functions Core Tools v4

## Setup

### 1. Create Azure AD App Registration

1. Go to [Azure Portal → App registrations → New registration](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
2. Name: `PresenceTracker` (or your preference)
3. Supported account types: **Single tenant**
4. After creation, note the **Application (client) ID** and **Directory (tenant) ID**
5. Go to **Certificates & secrets → New client secret** — save the secret value

### 2. Configure API Permissions

In the app registration, go to **API permissions → Add a permission → Microsoft Graph → Application permissions**:

| Permission | Purpose |
|---|---|
| `Presence.Read.All` | Read presence status for all users |
| `GroupMember.Read.All` | Read security group membership |

Click **Grant admin consent for [your tenant]**.

### 3. Create Azure SQL Database

1. Create an Azure SQL Database (Basic tier ~$5/month is sufficient)
2. Run the schema script: [`sql/001_CreateSchema.sql`](sql/001_CreateSchema.sql)
3. Note the connection string

### 4. Identify Your Security Group

1. In Azure Portal → Azure Active Directory → Groups
2. Find or create the security group containing users to track
3. Note the **Object ID**

### 5. Configure the Function

Edit `src/PresenceTracker/local.settings.json`:

```json
{
  "Values": {
    "AzureAd:TenantId": "your-tenant-id",
    "AzureAd:ClientId": "your-client-id",
    "AzureAd:ClientSecret": "your-client-secret",
    "SecurityGroupId": "your-security-group-object-id",
    "SqlConnectionString": "Server=tcp:yourserver.database.windows.net,1433;..."
  }
}
```

### 6. Run Locally

```bash
cd src/PresenceTracker
func start
```

## Power BI Connection

1. Open Power BI Desktop
2. **Get Data → Azure SQL Database**
3. Enter your server and database name
4. Select these views:

| View | Report Type |
|---|---|
| `vw_MonthlyPresenceSummary` | Monthly report: who spent how many hours in each status |
| `vw_DailyPresenceTimeline` | Daily drill-down: when each person was in each status |

### Monthly Report (vw_MonthlyPresenceSummary)

Columns available:
- `UserId`, `UserDisplayName`, `UserPrincipalName` — user identity
- `Year`, `Month` — time period
- `Availability` — status (Available, Busy, InAMeeting, Away, etc.)
- `TotalMinutes`, `TotalHours` — pre-aggregated duration

**Suggested visuals:**
- Stacked bar chart: X = User, Y = TotalHours, Legend = Availability
- Matrix: Rows = UserDisplayName, Columns = Month, Values = TotalHours

### Daily Report (vw_DailyPresenceTimeline)

Columns available:
- `UserId`, `UserDisplayName`, `UserPrincipalName` — user identity
- `Date` — calendar date
- `DetectedAtUtc`, `EndedAtUtc` — start/end of each status period
- `DurationMinutes` — how long
- `Availability`, `Activity` — what status

**Suggested visuals:**
- Gantt/timeline chart: show each user's status blocks throughout the day
- Table with conditional formatting on Availability

## Presence Status Values

| Availability | Meaning |
|---|---|
| `Available` | Online and available |
| `Busy` | In focused work |
| `InACall` | On a phone/Teams call |
| `InAMeeting` | In a scheduled meeting |
| `Presenting` | Sharing screen |
| `Focusing` | In focus mode |
| `DoNotDisturb` | DND enabled |
| `Away` | Inactive/idle |
| `BeRightBack` | Temporarily away |
| `Offline` | Signed out |
| `PresenceUnknown` | Status not determined |
| `Unknown` | Synthetic — indicates a monitoring gap |

## Data Model

Only **state changes** are stored. Duration is computed dynamically using SQL `LEAD()`:

```
User A: Available at 9:00 → Busy at 9:30 → InAMeeting at 10:00 → Available at 11:00
= 3 rows stored (not 120 rows for 120 minutes of polling)
```

The `vw_PresenceWithDuration` view adds `EndedAtUtc` and `DurationMinutes` by looking at the next row per user.

## Deployment to Azure

1. Create an Azure Function App (Consumption or Premium plan)
2. Configure application settings with the same keys from `local.settings.json`
3. Deploy:
   ```bash
   cd src/PresenceTracker
   func azure functionapp publish <your-function-app-name>
   ```

## Considerations

- **Polling interval**: Default is every 1 minute. Change the CRON in `PollPresenceFunction.cs` (`0 */1 * * * *`) to adjust.
- **Graph API limits**: The batch presence API supports up to 650 users per call. For larger groups, the function automatically batches.
- **Off-hours**: To skip polling nights/weekends, add a time-of-day check in the function or change the CRON expression.
- **Data retention**: With change-based storage (~60K rows/month for 100 users), the Basic SQL tier (2GB) holds years of data. Add a cleanup job if needed.
