using System;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Represents the dual 12-hour offset in-game time in Escape from Tarkov with day/night status.
    /// </summary>
    public record TarkovTimeDisplay(
        string PrimaryTime,      // "14:32" (UTC+3 * 7)
        string SecondaryTime,    // "02:32" (+12h offset)
        bool IsPrimaryDay,       // True if between 06:00 and 21:00
        bool IsSecondaryDay
    );

    /// <summary>
    /// Service for calculating Escape from Tarkov in-game time.
    /// Operates at a 7x speed multiplier relative to real time, anchored to Moscow UTC+3.
    /// </summary>
    public static class TarkovClockService
    {
        // 7x acceleration multiplier
        public const double TarkovTimeMultiplier = 7.0;

        /// <summary>
        /// Calculates current in-game times for Escape from Tarkov using current UTC time.
        /// </summary>
        public static TarkovTimeDisplay GetCurrentTimes()
        {
            return CalculateTarkovTime(DateTime.UtcNow);
        }

        /// <summary>
        /// Calculates current in-game times given an optional reference UTC DateTimeOffset.
        /// </summary>
        public static TarkovTimeDisplay GetCurrentTimes(DateTimeOffset? utcNow)
        {
            return CalculateTarkovTime((utcNow ?? DateTimeOffset.UtcNow).UtcDateTime);
        }

        /// <summary>
        /// Calculates in-game times for a specific UTC time.
        /// Real time moves at 7 Tarkov seconds per 1 real second from midnight Moscow time (UTC+3).
        /// </summary>
        public static TarkovTimeDisplay CalculateTarkovTime(DateTime utcTime)
        {
            // Moscow base offset is UTC+3
            var moscowTime = utcTime.AddHours(3);

            // Tarkov time: 7 seconds per 1 real second from midnight
            double secondsFromMidnight = moscowTime.TimeOfDay.TotalSeconds;
            double tarkovSeconds = (secondsFromMidnight * TarkovTimeMultiplier) % (24 * 3600);
            if (tarkovSeconds < 0) tarkovSeconds += 24 * 3600;

            TimeSpan primarySpan = TimeSpan.FromSeconds(tarkovSeconds);
            TimeSpan secondarySpan = TimeSpan.FromSeconds((tarkovSeconds + 12 * 3600) % (24 * 3600));

            string primaryStr = $"{(int)primarySpan.Hours:D2}:{primarySpan.Minutes:D2}";
            string secondaryStr = $"{(int)secondarySpan.Hours:D2}:{secondarySpan.Minutes:D2}";

            // Daytime: 06:00 to 21:00 (inclusive of 6, exclusive of 21)
            bool isPrimaryDay = primarySpan.Hours >= 6 && primarySpan.Hours < 21;
            bool isSecondaryDay = secondarySpan.Hours >= 6 && secondarySpan.Hours < 21;

            return new TarkovTimeDisplay(primaryStr, secondaryStr, isPrimaryDay, isSecondaryDay);
        }
    }
}
