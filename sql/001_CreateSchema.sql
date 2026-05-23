-- ============================================================
-- Schema for Teams Presence Tracker
-- Run this against your Azure SQL Database before deploying
-- ============================================================

-- Table: stores one row per presence status change per user
CREATE TABLE PresenceChanges (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId              NVARCHAR(36)    NOT NULL,
    UserDisplayName     NVARCHAR(256)   NULL,
    UserPrincipalName   NVARCHAR(256)   NULL,
    Availability        NVARCHAR(50)    NOT NULL,
    Activity            NVARCHAR(50)    NOT NULL,
    DetectedAtUtc       DATETIME2(0)    NOT NULL
);

-- Index: user-scoped time queries and LEAD() window function
CREATE NONCLUSTERED INDEX IX_UserId_DetectedAtUtc
    ON PresenceChanges (UserId, DetectedAtUtc);

-- Index: date-range scans for Power BI
CREATE NONCLUSTERED INDEX IX_DetectedAtUtc
    ON PresenceChanges (DetectedAtUtc)
    INCLUDE (UserId, Availability);
GO

-- ============================================================
-- View: Base view that computes duration via LEAD()
-- Each row gets an EndedAtUtc (from the next status change)
-- and DurationMinutes (difference between the two)
-- ============================================================
CREATE OR ALTER VIEW vw_PresenceWithDuration AS
SELECT
    Id,
    UserId,
    UserDisplayName,
    UserPrincipalName,
    Availability,
    Activity,
    DetectedAtUtc,
    LEAD(DetectedAtUtc) OVER (
        PARTITION BY UserId
        ORDER BY DetectedAtUtc
    ) AS EndedAtUtc,
    DATEDIFF(
        MINUTE,
        DetectedAtUtc,
        LEAD(DetectedAtUtc) OVER (
            PARTITION BY UserId
            ORDER BY DetectedAtUtc
        )
    ) AS DurationMinutes
FROM PresenceChanges;
GO

-- ============================================================
-- View: Monthly presence summary for Power BI
-- Shows total minutes and hours per user per status per month
-- Excludes synthetic "Unknown" gap markers
-- ============================================================
CREATE OR ALTER VIEW vw_MonthlyPresenceSummary AS
SELECT
    UserId,
    UserDisplayName,
    UserPrincipalName,
    YEAR(DetectedAtUtc)     AS [Year],
    MONTH(DetectedAtUtc)    AS [Month],
    Availability,
    SUM(DurationMinutes)    AS TotalMinutes,
    CAST(SUM(DurationMinutes) / 60.0 AS DECIMAL(10,2)) AS TotalHours
FROM vw_PresenceWithDuration
WHERE Availability <> 'Unknown'
  AND DurationMinutes IS NOT NULL
GROUP BY
    UserId,
    UserDisplayName,
    UserPrincipalName,
    YEAR(DetectedAtUtc),
    MONTH(DetectedAtUtc),
    Availability;
GO

-- ============================================================
-- View: Daily presence timeline for Power BI drill-down
-- Shows each status period with start, end, and duration
-- ============================================================
CREATE OR ALTER VIEW vw_DailyPresenceTimeline AS
SELECT
    UserId,
    UserDisplayName,
    UserPrincipalName,
    CAST(DetectedAtUtc AS DATE) AS [Date],
    DetectedAtUtc,
    EndedAtUtc,
    DurationMinutes,
    Availability,
    Activity
FROM vw_PresenceWithDuration;
GO
