using System;

namespace Domain.Common;

public static class PakistanTime
{
    private static readonly TimeZoneInfo PkTimeZone = ResolvePakistanTimeZone();

    private static TimeZoneInfo ResolvePakistanTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone(
                    "PKT",
                    TimeSpan.FromHours(5),
                    "Pakistan Standard Time",
                    "Pakistan Standard Time");
            }
        }
    }

    /// <summary>
    /// Threshold in days after which an active department is flagged as delayed.
    /// Default: 3.0 (3 days).
    /// For 10-minute testing, change to: 10.0 / 1440.0
    /// </summary>
    public const double DepartmentDelayThresholdDays = 3.0;

    public static DateTime Now =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PkTimeZone);

    public static DateTime Today => Now.Date;
}
