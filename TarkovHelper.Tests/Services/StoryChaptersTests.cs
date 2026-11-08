using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services;
using TarkovHelper.Services.DataAudit;
using Xunit;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseTests")]
    public class StoryChaptersTests : IDisposable
    {
        private readonly List<string> _tempFiles = new();

        public void Dispose()
        {
            foreach (var file in _tempFiles)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch { }
            }
        }

        private string CreateTempDatabase()
        {
            var tempDbPath = Path.Combine(Path.GetTempPath(), $"tarkov_test_{Guid.NewGuid():N}.db");
            _tempFiles.Add(tempDbPath);

            using var conn = new SqliteConnection($"Data Source={tempDbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA foreign_keys = ON;

                CREATE TABLE Quests (
                    Id TEXT PRIMARY KEY,
                    BsgId TEXT,
                    Name TEXT NOT NULL,
                    NameEN TEXT,
                    NameKO TEXT,
                    NameJA TEXT,
                    WikiPageLink TEXT,
                    Trader TEXT NOT NULL,
                    Location TEXT,
                    MinLevel INTEGER,
                    MinLevelApproved INTEGER DEFAULT 0,
                    MinLevelApprovedAt TEXT,
                    MinScavKarma REAL,
                    MinScavKarmaApproved INTEGER DEFAULT 0,
                    MinScavKarmaApprovedAt TEXT,
                    UpdatedAt TEXT NOT NULL,
                    KappaRequired INTEGER DEFAULT 0,
                    Faction TEXT,
                    IsApproved INTEGER DEFAULT 0,
                    ApprovedAt TEXT,
                    RequiredEdition TEXT,
                    RequiredEditionApproved INTEGER DEFAULT 0,
                    RequiredEditionApprovedAt TEXT,
                    ExcludedEdition TEXT,
                    ExcludedEditionApproved INTEGER DEFAULT 0,
                    ExcludedEditionApprovedAt TEXT,
                    RequiredDecodeCount INTEGER,
                    RequiredDecodeCountApproved INTEGER DEFAULT 0,
                    RequiredDecodeCountApprovedAt TEXT,
                    RequiredPrestigeLevel INTEGER,
                    RequiredPrestigeLevelApproved INTEGER DEFAULT 0,
                    RequiredPrestigeLevelApprovedAt TEXT
                );

                CREATE TABLE QuestRequirements (
                    Id TEXT PRIMARY KEY,
                    QuestId TEXT NOT NULL,
                    RequiredQuestId TEXT NOT NULL,
                    RequirementType TEXT NOT NULL DEFAULT 'Complete',
                    DelayMinutes INTEGER,
                    GroupId INTEGER DEFAULT 0,
                    IsApproved INTEGER DEFAULT 0,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE,
                    FOREIGN KEY (RequiredQuestId) REFERENCES Quests(Id) ON DELETE CASCADE
                );

                CREATE TABLE QuestRequiredItems (
                    Id TEXT PRIMARY KEY,
                    QuestId TEXT NOT NULL,
                    ItemId TEXT,
                    ItemName TEXT NOT NULL,
                    Count INTEGER NOT NULL DEFAULT 1,
                    RequiresFIR INTEGER NOT NULL DEFAULT 0,
                    RequirementType TEXT NOT NULL DEFAULT 'Required',
                    SortOrder INTEGER DEFAULT 0,
                    IsApproved INTEGER DEFAULT 0,
                    UpdatedAt TEXT NOT NULL,
                    DogtagMinLevel INTEGER,
                    DogtagFaction TEXT,
                    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE
                );

                CREATE TABLE HideoutStations (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    NameKO TEXT,
                    NameJA TEXT,
                    NormalizedName TEXT,
                    ImageLink TEXT,
                    MaxLevel INTEGER DEFAULT 0,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE HideoutLevels (
                    Id TEXT PRIMARY KEY,
                    StationId TEXT NOT NULL,
                    Level INTEGER NOT NULL,
                    ConstructionTime INTEGER DEFAULT 0,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
                );

                CREATE TABLE HideoutItemRequirements (
                    Id TEXT PRIMARY KEY,
                    StationId TEXT NOT NULL,
                    Level INTEGER NOT NULL,
                    ItemId TEXT NOT NULL,
                    ItemName TEXT NOT NULL,
                    ItemNameKO TEXT,
                    ItemNameJA TEXT,
                    IconLink TEXT,
                    Count INTEGER NOT NULL DEFAULT 1,
                    FoundInRaid INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER DEFAULT 0,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
                );

                -- Seed sample quests
                INSERT INTO Quests (Id, Name, Trader, MinLevel, UpdatedAt)
                VALUES ('quest_debut', 'Debut', 'Prapor', 1, '2026-01-01T00:00:00Z');

                INSERT INTO Quests (Id, Name, Trader, MinLevel, UpdatedAt)
                VALUES ('quest_checking', 'Checking', 'Prapor', 2, '2026-01-01T00:00:00Z');
            ";
            cmd.ExecuteNonQuery();

            return tempDbPath;
        }

        #region 1. DiffEngine Prerequisite Resilience Tests

        [Fact]
        public async Task DiffEngine_PrunesBrokenPrerequisiteLinks_WhenRequiredQuestIdIsUnknown()
        {
            var tempDb = CreateTempDatabase();
            var engine = new DatabaseDiffEngine();

            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = "quest_debut",
                        Name = "Debut",
                        Trader = "Prapor",
                        MinLevel = 1
                    }
                },
                QuestRequirements = new List<NormalizedQuestRequirement>
                {
                    // 1. Broken requirement pointing to completely non-existent phantom quest ID
                    new NormalizedQuestRequirement
                    {
                        Id = "req_broken_1",
                        QuestId = "quest_debut",
                        QuestName = "Debut",
                        RequiredQuestId = "phantom_quest_nonexistent_999",
                        RequiredQuestName = "Unknown Quest X",
                        RequirementType = "Complete"
                    },
                    // 2. Broken requirement where the source quest itself does not exist
                    new NormalizedQuestRequirement
                    {
                        Id = "req_broken_2",
                        QuestId = "phantom_quest_source_888",
                        QuestName = "Ghost Quest",
                        RequiredQuestId = "quest_debut",
                        RequiredQuestName = "Debut",
                        RequirementType = "Complete"
                    },
                    // 3. Valid requirement between existing local quests (Checking -> Debut)
                    new NormalizedQuestRequirement
                    {
                        Id = "req_valid_1",
                        QuestId = "quest_checking",
                        QuestName = "Checking",
                        RequiredQuestId = "quest_debut",
                        RequiredQuestName = "Debut",
                        RequirementType = "Complete"
                    }
                }
            };

            var diff = await engine.ComputeDiffAsync(remoteData, tempDb, pruneBrokenPrerequisites: true);

            // Assert: Broken requirements must be pruned and NEVER reach QuestRequirementDiffs
            Assert.NotNull(diff);
            Assert.DoesNotContain(diff.QuestRequirementDiffs, r => r.RequiredQuestId == "phantom_quest_nonexistent_999");
            Assert.DoesNotContain(diff.QuestRequirementDiffs, r => r.QuestId == "phantom_quest_source_888");

            // Valid requirement must be retained
            var validDiff = diff.QuestRequirementDiffs.FirstOrDefault(r => r.QuestId == "quest_checking" && r.RequiredQuestId == "quest_debut");
            Assert.NotNull(validDiff);
            Assert.Equal(DiffChangeType.Added, validDiff.ChangeType);
        }

        [Fact]
        public async Task DiffEngine_SectionA_PrunesPhantomPrerequisitesInAddedPrerequisitesList()
        {
            var tempDb = CreateTempDatabase();
            var engine = new DatabaseDiffEngine();

            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = "quest_debut",
                        Name = "Debut",
                        Trader = "Prapor",
                        MinLevel = 1
                    }
                },
                QuestRequirements = new List<NormalizedQuestRequirement>
                {
                    // Prerequisite pointing to phantom quest for Debut
                    new NormalizedQuestRequirement
                    {
                        Id = "req_phantom_sectionA",
                        QuestId = "quest_debut",
                        QuestName = "Debut",
                        RequiredQuestId = "phantom_ghost_prereq_id",
                        RequiredQuestName = "Ghost Prerequisite",
                        RequirementType = "Complete"
                    }
                }
            };

            var diff = await engine.ComputeDiffAsync(remoteData, tempDb, pruneBrokenPrerequisites: true);

            var debutDiff = diff.QuestDiffs.FirstOrDefault(q => q.QuestId == "quest_debut");
            if (debutDiff != null)
            {
                Assert.DoesNotContain("phantom_ghost_prereq_id", debutDiff.AddedPrerequisiteQuestIds);
            }
        }

        #endregion

        #region 2. TransactionApplier FK Protection Tests

        [Fact]
        public async Task TransactionApplier_MissingForeignKey_HandledGracefullyWithoutCrashing()
        {
            var tempDb = CreateTempDatabase();
            var applier = DatabaseTransactionApplier.Instance;

            var report = new DataDiffReport
            {
                DatabasePath = tempDb,
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "quest_tour",
                        QuestName = "Tour",
                        NewTrader = "Story",
                        NewMinLevel = 1,
                        // An added prerequisite that points to a non-existent quest
                        AddedPrerequisiteQuestIds = new List<string> { "phantom_prereq_not_in_db" }
                    }
                },
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    new QuestRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "req_phantom_insert",
                        QuestId = "quest_debut",
                        RequiredQuestId = "non_existent_target_quest",
                        NewRequirementType = "Complete"
                    }
                }
            };

            // ApplyDiffAsync must NOT throw foreign key violation error (SQLite error 19)
            // It must safely isolate the faulty requirement and commit valid changes
            var success = await applier.ApplyDiffAsync(report, tempDb, resilientPrerequisites: true);
            Assert.True(success, $"ApplyDiffAsync should return true but failed with: {applier.LastError}");

            // Verify quest_tour was successfully inserted
            using var conn = new SqliteConnection($"Data Source={tempDb}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'quest_tour';";
            var tourCount = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.Equal(1, tourCount);

            // Verify the phantom requirement was skipped (0 rows)
            cmd.CommandText = "SELECT COUNT(*) FROM QuestRequirements WHERE RequiredQuestId = 'non_existent_target_quest';";
            var reqCount = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.Equal(0, reqCount);
        }

        #endregion

        #region 3. QuestProgressService Story Chapter Decoupled Progression Tests

        [Fact]
        public async Task QuestProgressService_StoryChapters_AreActiveByDefault_EvenWithoutPrerequisites()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N");
            var storyId = $"story_tour_{testId}";
            var prereqId = $"unmet_prereq_{testId}";

            var storyTask = new TarkovTask
            {
                Ids = new List<string> { storyId },
                Name = "Tour",
                NormalizedName = storyId,
                Trader = "Story",
                RequiredLevel = 50, // Even with level requirement
                TaskRequirements = new List<TaskRequirement>
                {
                    new TaskRequirement
                    {
                        TaskId = prereqId,
                        TaskNormalizedName = prereqId,
                        Status = new List<string> { "complete" }
                    }
                }
            };

            var allTasks = new List<TarkovTask> { storyTask };
            await service.InitializeAsync(allTasks);

            // Story chapters are decoupled from sequential prerequisites: return QuestStatus.Active (never Locked)
            var status = service.GetStatus(storyTask);
            Assert.Equal(QuestStatus.Active, status);
        }

        [Fact]
        public async Task QuestProgressService_StoryChapters_CompleteNonRecursively()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N");
            var prereqId = $"prereq_quest_{testId}";
            var storyId = $"story_batya_{testId}";

            var prereqTask = new TarkovTask
            {
                Ids = new List<string> { prereqId },
                Name = "Prereq Quest",
                NormalizedName = prereqId,
                Trader = "Prapor"
            };

            var storyTask = new TarkovTask
            {
                Ids = new List<string> { storyId },
                Name = "Batya",
                NormalizedName = storyId,
                Trader = "Story",
                TaskRequirements = new List<TaskRequirement>
                {
                    new TaskRequirement
                    {
                        TaskId = prereqId,
                        TaskNormalizedName = prereqId,
                        Status = new List<string> { "complete" }
                    }
                }
            };

            var allTasks = new List<TarkovTask> { prereqTask, storyTask };
            await service.InitializeAsync(allTasks);

            // Complete story chapter with completePrerequisites = true
            service.CompleteQuest(storyTask, completePrerequisites: true);

            // Story chapter must be Done
            Assert.Equal(QuestStatus.Done, service.GetStatus(storyTask));

            // Prerequisite must NOT have been recursively completed
            Assert.NotEqual(QuestStatus.Done, service.GetStatus(prereqTask));
        }

        [Fact]
        public async Task QuestProgressService_CompleteDownstreamQuestWithPrerequisites_DoesNotCompleteStoryChapterPrerequisites()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N");
            var rootId = $"root_prereq_{testId}";
            var storyId = $"story_tour_{testId}";
            var downstreamId = $"uninvited_guests_1_{testId}";

            var rootPrereq = new TarkovTask
            {
                Ids = new List<string> { rootId },
                Name = "Root Prereq",
                NormalizedName = rootId,
                Trader = "Prapor"
            };

            var storyChapter = new TarkovTask
            {
                Ids = new List<string> { storyId },
                Name = "Tour",
                NormalizedName = storyId,
                Trader = "Story",
                TaskRequirements = new List<TaskRequirement>
                {
                    new TaskRequirement
                    {
                        TaskId = rootId,
                        TaskNormalizedName = rootId,
                        Status = new List<string> { "complete" }
                    }
                }
            };

            var downstreamQuest = new TarkovTask
            {
                Ids = new List<string> { downstreamId },
                Name = "Uninvited Guests - Part 1",
                NormalizedName = downstreamId,
                Trader = "Prapor",
                TaskRequirements = new List<TaskRequirement>
                {
                    new TaskRequirement
                    {
                        TaskId = storyId,
                        TaskNormalizedName = storyId,
                        Status = new List<string> { "complete" }
                    }
                }
            };

            var allTasks = new List<TarkovTask> { rootPrereq, storyChapter, downstreamQuest };
            await service.InitializeAsync(allTasks);

            // Verify initial states
            Assert.Equal(QuestStatus.Active, service.GetStatus(rootPrereq));
            Assert.Equal(QuestStatus.Active, service.GetStatus(storyChapter));
            Assert.Equal(QuestStatus.Locked, service.GetStatus(downstreamQuest));

            // Complete downstream quest with completePrerequisites = true
            service.CompleteQuest(downstreamQuest, completePrerequisites: true);

            // Assert: Downstream quest is Done
            Assert.Equal(QuestStatus.Done, service.GetStatus(downstreamQuest));

            // Assert: Intermediate Story Chapter is Done (cascaded from downstreamQuest)
            Assert.Equal(QuestStatus.Done, service.GetStatus(storyChapter));

            // Assert: Root Prereq of Story Chapter is NOT marked Done (cascade stopped at Story Chapter)
            Assert.NotEqual(QuestStatus.Done, service.GetStatus(rootPrereq));
            Assert.Equal(QuestStatus.Active, service.GetStatus(rootPrereq));
        }

        #endregion

        #region 4. Reactive Unlock Propagation & Manual Override Tests

        [Fact]
        public async Task QuestProgressService_StoryChapter_UnlockPropagation_UnlocksAndRelocksDependentQuest()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N");
            var storyId = $"story_tour_{testId}";
            var downstreamId = $"uninvited_guests_1_{testId}";

            var tourTask = new TarkovTask
            {
                Ids = new List<string> { storyId },
                Name = "Tour",
                NormalizedName = storyId,
                Trader = "Story"
            };

            var dependentTask = new TarkovTask
            {
                Ids = new List<string> { downstreamId },
                Name = "Uninvited Guests - Part 1",
                NormalizedName = downstreamId,
                Trader = "Prapor",
                TaskRequirements = new List<TaskRequirement>
                {
                    new TaskRequirement
                    {
                        TaskId = storyId,
                        TaskNormalizedName = storyId,
                        Status = new List<string> { "complete" }
                    }
                }
            };

            var allTasks = new List<TarkovTask> { tourTask, dependentTask };
            await service.InitializeAsync(allTasks);

            // Initially: Tour is Active, dependent quest is Locked
            Assert.Equal(QuestStatus.Active, service.GetStatus(tourTask));
            Assert.Equal(QuestStatus.Locked, service.GetStatus(dependentTask));

            int progressChangedEventCount = 0;
            service.ProgressChanged += (s, e) => progressChangedEventCount++;

            // Step 1: Mark Tour Done -> Triggers ProgressChanged and unlocks dependent quest
            service.CompleteQuest(tourTask, completePrerequisites: false);

            Assert.True(progressChangedEventCount > 0, "ProgressChanged event should be fired on completion.");
            Assert.Equal(QuestStatus.Done, service.GetStatus(tourTask));
            Assert.Equal(QuestStatus.Active, service.GetStatus(dependentTask));

            // Step 2: Unmark Tour -> Triggers ProgressChanged and relocks dependent quest
            int eventCountBeforeReset = progressChangedEventCount;
            service.ResetQuest(tourTask);

            Assert.True(progressChangedEventCount > eventCountBeforeReset, "ProgressChanged event should be fired on reset.");
            Assert.Equal(QuestStatus.Active, service.GetStatus(tourTask));
            Assert.Equal(QuestStatus.Locked, service.GetStatus(dependentTask));
        }

        [Fact]
        public async Task QuestProgressService_SetQuestActiveOverride_MarksQuestActiveAndTriggersProgressChanged()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N");
            var unmetId = $"unmet_req_{testId}";
            var lockedId = $"locked_quest_{testId}";

            var unmetPrereqTask = new TarkovTask
            {
                Ids = new List<string> { unmetId },
                Name = "Unmet Prereq",
                NormalizedName = unmetId,
                Trader = "Prapor"
            };

            var lockedQuest = new TarkovTask
            {
                Ids = new List<string> { lockedId },
                Name = "Locked Quest",
                NormalizedName = lockedId,
                Trader = "Mechanic",
                TaskRequirements = new List<TaskRequirement>
                {
                    new TaskRequirement
                    {
                        TaskId = unmetId,
                        TaskNormalizedName = unmetId,
                        Status = new List<string> { "complete" }
                    }
                }
            };

            var allTasks = new List<TarkovTask> { unmetPrereqTask, lockedQuest };
            await service.InitializeAsync(allTasks);

            // Initially Locked because unmetPrereqTask is not Done
            Assert.Equal(QuestStatus.Locked, service.GetStatus(lockedQuest));

            int eventCount = 0;
            service.ProgressChanged += (s, e) => eventCount++;

            // Enable manual Active override
            await service.SetQuestActiveOverrideAsync(lockedId, true);

            Assert.True(eventCount > 0, "ProgressChanged should fire when setting override.");
            Assert.Equal(QuestStatus.Active, service.GetStatus(lockedQuest));

            // Clear manual Active override
            await service.SetQuestActiveOverrideAsync(lockedId, false);

            Assert.True(eventCount > 1, "ProgressChanged should fire when clearing override.");
            Assert.Equal(QuestStatus.Locked, service.GetStatus(lockedQuest));
        }

        #endregion
    }
}
