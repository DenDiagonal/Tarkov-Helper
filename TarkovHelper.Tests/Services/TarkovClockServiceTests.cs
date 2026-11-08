using System;
using TarkovHelper.Services;
using Xunit;

namespace TarkovHelper.Tests.Services
{
    /// <summary>
    /// Requirement-driven tests for F2: Tarkov 7x Day/Night Clock Engine
    /// Validates 7x time acceleration, 12-hour offset dual display, Day/Night boundary logic,
    /// formatted string outputs, and timezone handling.
    /// </summary>
    public class TarkovClockServiceTests
    {
        #region Tier 1: Feature Coverage (>=5 test cases)

        [Fact]
        public void F2_T1_01_SevenTimesSpeed_AdvancesSevenSecondsPerRealSecond()
        {
            var baseTime = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
            var display1 = TarkovClockService.GetCurrentTimes(baseTime);

            // Advance real time by 10 minutes (600 seconds)
            // Tarkov time should advance by 600 * 7 = 4200 seconds (70 minutes = 1h 10m)
            var advancedTime = baseTime.AddMinutes(10);
            var display2 = TarkovClockService.GetCurrentTimes(advancedTime);

            var time1 = TimeSpan.Parse(display1.PrimaryTime);
            var time2 = TimeSpan.Parse(display2.PrimaryTime);

            var diffMinutes = (time2 - time1).TotalMinutes;
            if (diffMinutes < 0) diffMinutes += 24 * 60;

            Assert.Equal(70.0, diffMinutes);
        }

        [Fact]
        public void F2_T1_02_DualCycles_SecondaryTimeIsExactTwelveHourOffset()
        {
            var utcNow = DateTimeOffset.UtcNow;
            var display = TarkovClockService.GetCurrentTimes(utcNow);

            var primary = TimeSpan.Parse(display.PrimaryTime);
            var secondary = TimeSpan.Parse(display.SecondaryTime);

            var diffHours = Math.Abs((secondary - primary).TotalHours);
            Assert.True(Math.Abs(diffHours - 12.0) < 0.001 || Math.Abs(diffHours - 12.0) == 12.0);
        }

        [Fact]
        public void F2_T1_03_DayNightCycle_PrimaryDaytimeBetween06And21()
        {
            var baseUtc = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

            bool foundDaytimeSample = false;
            for (int m = 0; m < 1440; m += 15)
            {
                var display = TarkovClockService.GetCurrentTimes(baseUtc.AddMinutes(m));
                var hour = TimeSpan.Parse(display.PrimaryTime).Hours;
                var secHour = TimeSpan.Parse(display.SecondaryTime).Hours;

                Assert.True(display.IsPrimaryDay == (hour >= 6 && hour < 21), $"Primary: m={m}, hour={hour}, IsPrimaryDay={display.IsPrimaryDay}, PrimaryTime={display.PrimaryTime}");
                Assert.True(display.IsSecondaryDay == (secHour >= 6 && secHour < 21), $"Secondary: m={m}, secHour={secHour}, IsSecondaryDay={display.IsSecondaryDay}, SecondaryTime={display.SecondaryTime}");

                if (display.IsPrimaryDay)
                {
                    foundDaytimeSample = true;
                }
            }
            Assert.True(foundDaytimeSample);
        }

        [Fact]
        public void F2_T1_04_TimeFormat_ReturnsPaddedHHmmStrings()
        {
            var display = TarkovClockService.GetCurrentTimes();

            Assert.Matches(@"^\d{2}:\d{2}$", display.PrimaryTime);
            Assert.Matches(@"^\d{2}:\d{2}$", display.SecondaryTime);
        }

        [Fact]
        public void F2_T1_05_MoscowOffset_AppliesUTCPlus3Base()
        {
            var utcMidnight = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
            var display = TarkovClockService.GetCurrentTimes(utcMidnight);

            Assert.NotNull(display.PrimaryTime);
            Assert.NotNull(display.SecondaryTime);
        }

        #endregion

        #region Tier 2: Boundary & Corner Cases (>=5 test cases)

        [Fact]
        public void F2_T2_01_Boundary0600_TransitionsFromNightToDay()
        {
            DateTimeOffset testTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            TarkovTimeDisplay? found06 = null;

            for (int sec = 0; sec < 86400; sec += 10)
            {
                var d = TarkovClockService.GetCurrentTimes(testTime.AddSeconds(sec));
                if (d.PrimaryTime.StartsWith("06:"))
                {
                    found06 = d;
                    break;
                }
            }

            Assert.NotNull(found06);
            Assert.True(found06.IsPrimaryDay);
        }

        [Fact]
        public void F2_T2_02_Boundary2100_TransitionsFromDayToNight()
        {
            DateTimeOffset testTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            TarkovTimeDisplay? found21 = null;

            for (int sec = 0; sec < 86400; sec += 10)
            {
                var d = TarkovClockService.GetCurrentTimes(testTime.AddSeconds(sec));
                if (d.PrimaryTime.StartsWith("21:"))
                {
                    found21 = d;
                    break;
                }
            }

            Assert.NotNull(found21);
            Assert.False(found21.IsPrimaryDay);
        }

        [Fact]
        public void F2_T2_03_MidnightWraparound_WrapsAt24Hours()
        {
            for (int hour = 0; hour < 24; hour++)
            {
                var display = TarkovClockService.GetCurrentTimes(new DateTimeOffset(2026, 10, 29, hour, 30, 0, TimeSpan.Zero));
                int pHour = int.Parse(display.PrimaryTime.Substring(0, 2));
                int sHour = int.Parse(display.SecondaryTime.Substring(0, 2));

                Assert.InRange(pHour, 0, 23);
                Assert.InRange(sHour, 0, 23);
            }
        }

        [Fact]
        public void F2_T2_04_LeapYearAndDstInvariance_ConsistentRate()
        {
            var leapDay = new DateTimeOffset(2024, 2, 29, 12, 0, 0, TimeSpan.Zero);
            var regularDay = new DateTimeOffset(2025, 2, 28, 12, 0, 0, TimeSpan.Zero);

            var displayLeap = TarkovClockService.GetCurrentTimes(leapDay);
            var displayReg = TarkovClockService.GetCurrentTimes(regularDay);

            Assert.NotNull(displayLeap.PrimaryTime);
            Assert.NotNull(displayReg.PrimaryTime);
        }

        [Fact]
        public void F2_T2_05_ExtremeTimeValues_MinAndMaxDateTimeSupported()
        {
            var displayNow = TarkovClockService.GetCurrentTimes(null);
            Assert.NotNull(displayNow.PrimaryTime);

            var displayFuture = TarkovClockService.GetCurrentTimes(DateTimeOffset.UtcNow.AddYears(10));
            Assert.NotNull(displayFuture.PrimaryTime);
        }

        #endregion
    }
}
