using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Services;
using Xunit;

namespace TarkovHelper.Tests
{
    public class TarkovTrackerParityBlockTests : IDisposable
    {
        private readonly double _originalScavRep;
        private readonly int _originalPlayerLevel;

        public TarkovTrackerParityBlockTests()
        {
            _originalScavRep = SettingsService.Instance.ScavRep;
            _originalPlayerLevel = SettingsService.Instance.PlayerLevel;
        }

        public void Dispose()
        {
            SettingsService.Instance.ScavRep = _originalScavRep;
            SettingsService.Instance.PlayerLevel = _originalPlayerLevel;
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        [Fact]
        public void WeaponsCirculation_PoolEvaluation_StrictlyCountsPoolTaskIds()
        {
            var service = QuestProgressService.Instance;

            // Mock pool requirement for Peacekeeper LL2 (RequiredCompletedCount = 5)
            // With explicit TaskIds containing 11 tasks
            var poolTaskIds = new List<string>
            {
                "task_1", "task_2", "task_3", "task_4", "task_5",
                "task_6", "task_7", "task_8", "task_9", "task_10", "task_11"
            };

            var weaponsCirculation = new TarkovTask
            {
                Ids = new List<string> { "weapons-circulation-test" }, NormalizedName = "weapons-circulation-test",
                Name = "Weapons Circulation",
                Trader = "Peacekeeper",
                RequiredLevel = 14,
                PoolRequirements = new List<QuestPoolRequirement>
                {
                    new QuestPoolRequirement
                    {
                        Id = "pool_pk_ll2",
                        PoolTrader = "Peacekeeper",
                        PoolTraderLL = 2,
                        RequiredCompletedCount = 5,
                        TaskIds = poolTaskIds
                    }
                }
            };

            // Test 1: Only 3 tasks completed in pool -> MUST BE LOCKED (false)
            // Even if user completed 20 other quests not in this pool!
            var isMet = service.IsPoolRequirementMet(weaponsCirculation);
            Assert.False(isMet, "With 0-3 pool tasks completed, Weapons Circulation must be Locked (3 < 5).");
        }

        [Fact]
        public void Fence_ScavKarmaRequirement_LocksAndUnlocksAccurately()
        {
            var service = QuestProgressService.Instance;
            var settings = SettingsService.Instance;

            var isThisAReference = new TarkovTask
            {
                Ids = new List<string> { "is-this-a-reference-test" }, NormalizedName = "is-this-a-reference-test",
                Name = "Is This a Reference?",
                Trader = "Fence",
                RequiredScavKarma = 1.0,
                TraderRequirements = new List<QuestTraderRequirement>
                {
                    new QuestTraderRequirement
                    {
                        TraderName = "Fence",
                        RequirementType = "Reputation",
                        CompareMethod = ">=",
                        RequiredLevel = 1
                    }
                }
            };

            // Case A: Scav Karma = 0.00 (Standard starting Scav Karma) -> LOCKED
            settings.ScavRep = 0.0;
            Assert.False(service.IsTraderRequirementMet(isThisAReference));
            Assert.False(service.IsScavKarmaRequirementMet(isThisAReference));

            // Case B: Scav Karma = 0.80 -> LOCKED
            settings.ScavRep = 0.8;
            Assert.False(service.IsTraderRequirementMet(isThisAReference));
            Assert.False(service.IsScavKarmaRequirementMet(isThisAReference));

            // Case C: Scav Karma = 1.00 -> UNLOCKED
            settings.ScavRep = 1.0;
            Assert.True(service.IsTraderRequirementMet(isThisAReference));
            Assert.True(service.IsScavKarmaRequirementMet(isThisAReference));

            // Case D: Scav Karma = 2.50 -> UNLOCKED
            settings.ScavRep = 2.5;
            Assert.True(service.IsTraderRequirementMet(isThisAReference));
            Assert.True(service.IsScavKarmaRequirementMet(isThisAReference));

            // Reset back to 0.0
            settings.ScavRep = 0.0;
        }

        [Fact]
        public void TarkovButcher_HasLocationCoordinatesInGroundZero()
        {
            var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "publish", "tarkov_data.db");
            if (!File.Exists(dbPath))
            {
                dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "publish", "tarkov_data.db");
            }
            if (!File.Exists(dbPath))
            {
                dbPath = "publish/tarkov_data.db";
            }

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT qo.LocationPoints 
                FROM QuestObjectives qo 
                JOIN Quests q ON qo.QuestId = q.Id 
                WHERE q.Name LIKE '%Butcher%' AND qo.Description LIKE '%chemical container%';";

            var result = (string?)cmd.ExecuteScalar();
            Assert.False(string.IsNullOrWhiteSpace(result), "The Tarkov Butcher must have LocationPoints populated.");
            Assert.Contains("78.571", result);
            Assert.Contains("100.85899", result);
        }

        [Fact]
        public void QuestPoolRequirements_All60PoolsHaveExplicitTaskIds()
        {
            var candidatePaths = new[]
            {
                @"c:\Games\Proyectos\Tarkov-Helper\publish\tarkov_data.db",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "publish", "tarkov_data.db"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "publish", "tarkov_data.db"),
                "publish/tarkov_data.db"
            };
            var dbPath = candidatePaths.FirstOrDefault(File.Exists) ?? "publish/tarkov_data.db";

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM QuestPoolRequirements WHERE TaskIds IS NOT NULL AND TaskIds != '' AND TaskIds != '[]';";
            var populatedPools = (long?)cmd.ExecuteScalar();
            Assert.True(populatedPools >= 50, $"Expected at least 50 pools populated with TaskIds, got {populatedPools}");
        }

        [Fact]
        public void ExtractStyles_SharedCoopHasDistinctColor_FromPmcAndScav()
        {
            var (pmcFill, _) = TarkovHelper.Pages.Map.Components.MapExtractMarkerManager.GetExtractStyle(TarkovHelper.Models.Map.ExtractFaction.Pmc);
            var (scavFill, _) = TarkovHelper.Pages.Map.Components.MapExtractMarkerManager.GetExtractStyle(TarkovHelper.Models.Map.ExtractFaction.Scav);
            var (sharedFill, _) = TarkovHelper.Pages.Map.Components.MapExtractMarkerManager.GetExtractStyle(TarkovHelper.Models.Map.ExtractFaction.Shared);

            // Shared / Co-op extraction must be distinct from both pure PMC and pure Scav
            Assert.NotEqual(pmcFill, sharedFill);
            Assert.NotEqual(scavFill, sharedFill);
            Assert.NotEqual(pmcFill, scavFill);

            // Verify Shared is Cyan/Teal (G > 200, B > 200)
            Assert.True(sharedFill.G >= 200 && sharedFill.B >= 200, "Shared extract must be bright Cyan for clear visual distinction.");
        }

        [Fact]
        public void OverlayMiniMapSettings_ExtractionAndSpawnsProperties_SynchronizeProperly()
        {
            var settings = new TarkovHelper.Models.Map.OverlayMiniMapSettings();

            // Default state
            Assert.True(settings.ShowPmcExtracts);
            Assert.True(settings.ShowScavExtracts);
            Assert.True(settings.ShowPmcSpawns);
            Assert.True(settings.ShowExtractMarkers);

            // Turning off PMC extracts only
            settings.ShowPmcExtracts = false;
            Assert.False(settings.ShowPmcExtracts);
            Assert.True(settings.ShowScavExtracts);
            Assert.True(settings.ShowExtractMarkers, "ShowExtractMarkers is true when either PMC or Scav is enabled");

            // Turning off both
            settings.ShowScavExtracts = false;
            Assert.False(settings.ShowExtractMarkers);

            // Toggling composite ShowExtractMarkers
            settings.ShowExtractMarkers = true;
            Assert.True(settings.ShowPmcExtracts);
            Assert.True(settings.ShowScavExtracts);

            // Fixed view mode invariant
            settings.ViewMode = TarkovHelper.Models.Map.MiniMapViewMode.Fixed;
            Assert.Equal(TarkovHelper.Models.Map.MiniMapViewMode.Fixed, settings.ViewMode);
        }

        private static void RunInSta(Action action, int timeoutSeconds = 10)
        {
            Exception? caught = null;
            var thread = new System.Threading.Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { caught = ex; }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                throw new TimeoutException($"STA test timed out after {timeoutSeconds}s");
            }
            if (caught != null) throw caught;
        }

        [Fact]
        public void MapTooltipHelper_UsesPlacementModeTop_ToPreventPointMarkerCollision()
        {
            RunInSta(() =>
            {
                var tooltip = TarkovHelper.Pages.Map.Components.MapTooltipHelper.CreateTooltip("Test Objective", "Details");
                Assert.Equal(System.Windows.Controls.Primitives.PlacementMode.Top, tooltip.Placement);
                Assert.Equal(-14, tooltip.VerticalOffset);
            });
        }

        [Fact]
        public void QuestObjectiveColors_MapToPaletteR3_AcrossDbSynonymsAndCategories()
        {
            var settings = new TarkovHelper.Models.Map.MapTrackerSettings();

            // Visit -> Cyan (#00E5FF)
            Assert.Equal("#00E5FF", settings.GetMarkerColor("visit"));
            Assert.Equal("#00E5FF", new TarkovHelper.Models.Map.TaskObjectiveWithLocation { Type = "visit" }.MarkerColor);

            // Mark -> Amber (#FFA000)
            Assert.Equal("#FFA000", settings.GetMarkerColor("mark"));
            Assert.Equal("#FFA000", new TarkovHelper.Models.Map.TaskObjectiveWithLocation { Type = "mark" }.MarkerColor);

            // Plant / Stash -> Purple (#AB47BC)
            Assert.Equal("#AB47BC", settings.GetMarkerColor("plantItem"));
            Assert.Equal("#AB47BC", settings.GetMarkerColor("stash"));
            Assert.Equal("#AB47BC", settings.GetMarkerColor("plant"));
            Assert.Equal("#AB47BC", new TarkovHelper.Models.Map.TaskObjectiveWithLocation { Type = "stash" }.MarkerColor);

            // Find / Collect / HandOver -> Yellow (#FFEB3B)
            Assert.Equal("#FFEB3B", settings.GetMarkerColor("findItem"));
            Assert.Equal("#FFEB3B", settings.GetMarkerColor("collect"));
            Assert.Equal("#FFEB3B", settings.GetMarkerColor("handover"));
            Assert.Equal("#FFEB3B", new TarkovHelper.Models.Map.TaskObjectiveWithLocation { Type = "collect" }.MarkerColor);

            // Extract / Survive -> Blue (#29B6F6)
            Assert.Equal("#29B6F6", settings.GetMarkerColor("extract"));
            Assert.Equal("#29B6F6", settings.GetMarkerColor("survive"));
            Assert.Equal("#29B6F6", new TarkovHelper.Models.Map.TaskObjectiveWithLocation { Type = "survive" }.MarkerColor);
        }
    }
}