using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services.DataAudit
{
    /// <summary>
    /// Executes atomic SQLite database transactions to apply DataDiffReport changes to tarkov_data.db.
    /// Strictly enforces foreign keys (PRAGMA foreign_keys = ON), automatically creates timestamped backups
    /// before any modifications, ensures safe defaults for spatial coordinates and approved flags,
    /// rolls back completely on any error with zero partial writes, and orchestrates comprehensive
    /// in-memory hot-reload without application restart.
    /// </summary>
    public class DatabaseTransactionApplier
    {
        private static readonly HashSet<string> KnownNonSurviveQuestIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "608974d01a66564e74191fc0", "5eda19f0edce541157209cee", "5ae448e586f7744dcf0c2a67",
            "59c124d686f774189b3c843f", "64ee99639878a0569d6ec8c9", "639dbaf17c898a131e1cffff",
            "64f5aac4b63b74469b6c14c2", "5a68665c86f774255929b4c7", "639135c3744e452011470807",
            "639136f086e646067c176a8b", "64f731ab83cfca080a361e42", "63966ff54c3ef01b6f3ffad8",
            "5edabd13218d181e29451442", "5fd9fad9c1ce6b1a3b486d00", "5ac3467986f7741d6224abc2",
            "5a03296886f774569778596a", "6396700fea19ac7ed845db32", "5ae448f286f77448d73c0131",
            "5c10f94386f774227172c572", "5ede55112c95834b583f052a", "64ee9df4496db64f9b7a4432",
            "63967028c4a91c5cb76abd81", "608974af4b05530f55550c21"
        };

        private static readonly ILogger _log = Log.For<DatabaseTransactionApplier>();
        private static DatabaseTransactionApplier? _instance;
        public static DatabaseTransactionApplier Instance => _instance ??= new DatabaseTransactionApplier();

        private static readonly SemaphoreSlim _applierLock = new(1, 1);

        /// <summary>
        /// Absolute path of the last backup created prior to a transaction execution.
        /// </summary>
        public string? LastBackupPath { get; private set; }

        /// <summary>
        /// Error message of the last failed transaction, if any.
        /// </summary>
        public string? LastError { get; private set; }

        /// <summary>
        /// When true, prerequisite foreign key constraint failures will be isolated and skipped rather than aborting the entire transaction.
        /// </summary>
        public bool ResilientPrerequisites { get; set; } = true;

        /// <summary>
        /// Event fired immediately after a transaction commits and hot-reload is initiated.
        /// </summary>
        public event EventHandler? DiffApplied;

        /// <summary>
        /// Enforces strict isolation: user_data.db must never be touched by DatabaseTransactionApplier.
        /// </summary>
        private static void ValidateNotUserDataDb(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var normalized = path.Replace('/', '\\').ToLowerInvariant();
            if (normalized.EndsWith("user_data.db") ||
                normalized.Contains("\\user_data.db") ||
                normalized.Contains("userdata.db"))
            {
                throw new InvalidOperationException("Security & Integrity Violation: Operations on 'user_data.db' are strictly forbidden in DatabaseTransactionApplier.");
            }
        }

        /// <summary>
        /// Applies all classified changes in the diff report to the target SQLite database within a single atomic transaction.
        /// </summary>
        /// <param name="diff">The classified data diff report to apply.</param>
        /// <param name="targetDbPath">Optional path to target database; defaults to Assets/tarkov_data.db.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>True if all changes were applied and committed successfully; false if rolled back due to error.</returns>
        public async Task<bool> ApplyDiffAsync(DataDiffReport diff, string? targetDbPath, CancellationToken ct)
            => await ApplyDiffAsync(diff, targetDbPath, null, ct);

        /// <summary>
        /// Applies all classified changes in the diff report to the target SQLite database within a single atomic transaction.
        /// </summary>
        /// <param name="diff">The classified data diff report to apply.</param>
        /// <param name="targetDbPath">Optional path to target database; defaults to Assets/tarkov_data.db.</param>
        /// <param name="resilientPrerequisites">Optional override to isolate prerequisite foreign key failures rather than aborting transaction.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>True if all changes were applied and committed successfully; false if rolled back due to error.</returns>
        public async Task<bool> ApplyDiffAsync(DataDiffReport diff, string? targetDbPath = null, bool? resilientPrerequisites = null, CancellationToken ct = default)
        {
            if (diff == null)
                throw new ArgumentNullException(nameof(diff));

            LastError = null;
            LastBackupPath = null;

            if (!diff.HasChanges)
            {
                _log.Info("ApplyDiffAsync: Diff report contains 0 changes. Nothing to apply.");
                return true;
            }

            var resolvedDbPath = DatabaseBackupManager.ResolveDatabasePath(targetDbPath);
            ValidateNotUserDataDb(resolvedDbPath);

            if (!File.Exists(resolvedDbPath))
            {
                throw new FileNotFoundException($"Target database file not found: {resolvedDbPath}", resolvedDbPath);
            }

            await _applierLock.WaitAsync(ct);
            try
            {
                // 1. Create a dated pre-transaction backup
                _log.Info($"Creating pre-transaction backup for '{resolvedDbPath}'...");
                LastBackupPath = await DatabaseBackupManager.Instance.CreateBackupAsync(resolvedDbPath);
                _log.Info($"Pre-transaction backup successfully created at: {LastBackupPath}");

                // 2. Open SQLite connection with ReadWrite mode and DefaultTimeout = 30
                var csb = new SqliteConnectionStringBuilder
                {
                    DataSource = resolvedDbPath,
                    Mode = SqliteOpenMode.ReadWrite,
                    DefaultTimeout = 30
                };

                await using var connection = new SqliteConnection(csb.ConnectionString);
                await connection.OpenAsync(ct);

                // 3. Strictly enforce foreign keys
                await using (var pragmaCmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
                {
                    await pragmaCmd.ExecuteNonQueryAsync(ct);
                }

                // 4. Wrap all modifications in a single atomic transaction
                await using var transaction = connection.BeginTransaction();
                try
                {
                    // Sanitize any historical orphan records from legacy migrations
                    if (await TableExistsAsync(connection, transaction, "QuestRequiredItems", ct))
                    {
                        await using var cCmd = new SqliteCommand("DELETE FROM QuestRequiredItems WHERE QuestId NOT IN (SELECT Id FROM Quests);", connection, transaction);
                        await cCmd.ExecuteNonQueryAsync(ct);
                    }
                    if (await TableExistsAsync(connection, transaction, "QuestRequirements", ct))
                    {
                        await using var cCmd = new SqliteCommand("DELETE FROM QuestRequirements WHERE QuestId NOT IN (SELECT Id FROM Quests) OR RequiredQuestId NOT IN (SELECT Id FROM Quests);", connection, transaction);
                        await cCmd.ExecuteNonQueryAsync(ct);
                    }

                    if (await TableExistsAsync(connection, transaction, "QuestObjectives", ct))
                    {
                        await using var cCmd = new SqliteCommand("DELETE FROM QuestObjectives WHERE QuestId NOT IN (SELECT Id FROM Quests);", connection, transaction);
                        await cCmd.ExecuteNonQueryAsync(ct);
                    }
                    if (await TableExistsAsync(connection, transaction, "QuestTraderRequirements", ct))
                    {
                        await using var cCmd = new SqliteCommand("DELETE FROM QuestTraderRequirements WHERE QuestId NOT IN (SELECT Id FROM Quests);", connection, transaction);
                        await cCmd.ExecuteNonQueryAsync(ct);
                    }
                    if (await TableExistsAsync(connection, transaction, "QuestPoolRequirements", ct))
                    {
                        await using var cCmd = new SqliteCommand("DELETE FROM QuestPoolRequirements WHERE QuestId NOT IN (SELECT Id FROM Quests);", connection, transaction);
                        await cCmd.ExecuteNonQueryAsync(ct);
                    }
                    if (await TableExistsAsync(connection, transaction, "OptionalQuests", ct))
                    {
                        await using var cCmd = new SqliteCommand("DELETE FROM OptionalQuests WHERE QuestId NOT IN (SELECT Id FROM Quests) OR AlternativeQuestId NOT IN (SELECT Id FROM Quests);", connection, transaction);
                        await cCmd.ExecuteNonQueryAsync(ct);
                    }

                    // A. Hideout Stations
                    _log.Info("Applying HideoutStations...");
                    await ApplyHideoutStationsAsync(connection, transaction, diff, ct);

                    // B. Hideout Levels
                    _log.Info("Applying HideoutLevels...");
                    await ApplyHideoutLevelsAsync(connection, transaction, diff, ct);

                    // C. Hideout Item Requirements
                    _log.Info("Applying HideoutItemRequirements...");
                    await ApplyHideoutItemRequirementsAsync(connection, transaction, diff, ct);

                    bool isResilient = resilientPrerequisites ?? ResilientPrerequisites;

                    // D. Quests
                    _log.Info("Applying Quests...");
                    await ApplyQuestsAsync(connection, transaction, diff, isResilient, ct);

                    // E. Quest Requirements (prerequisites)
                    _log.Info("Applying QuestRequirements...");
                    await ApplyQuestRequirementsAsync(connection, transaction, diff, isResilient, ct);

                    // F. Quest Required Items
                    _log.Info("Applying QuestRequiredItems...");
                    await ApplyQuestRequiredItemsAsync(connection, transaction, diff, ct);

                    // G. Quest Trader Requirements
                    _log.Info("Applying QuestTraderRequirements...");
                    await ApplyQuestTraderRequirementsAsync(connection, transaction, diff, isResilient, ct);

                    // H. Quest Pool Requirements
                    _log.Info("Applying QuestPoolRequirements...");
                    await ApplyQuestPoolRequirementsAsync(connection, transaction, diff, isResilient, ct);

                    // Commit transaction
                    await transaction.CommitAsync(ct);
                    _log.Info($"Successfully committed all {diff.TotalChanges} changes to '{resolvedDbPath}'.");
                }
                catch (Exception ex)
                {
                    _log.Error($"Exception during transaction execution, rolling back immediately: {ex.Message}\n{ex.StackTrace}");
                    LastError = ex.Message;
                    try
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                        _log.Info("Transaction rolled back successfully. Target database remains 100% unchanged.");
                    }
                    catch (Exception rbEx)
                    {
                        _log.Error($"Error while executing rollback: {rbEx.Message}");
                    }
                    return false;
                }

                // 5. Clear SQLite connection pools to release file locks
                SqliteConnection.ClearAllPools();

                // 6. Comprehensive hot-reload orchestration
                await OrchestrateHotReloadAsync();

                DiffApplied?.Invoke(this, EventArgs.Empty);
                return true;
            }
            finally
            {
                _applierLock.Release();
            }
        }

        #region Entity Application Handlers

        private async Task ApplyQuestsAsync(SqliteConnection connection, SqliteTransaction transaction, DataDiffReport diff, bool isResilient, CancellationToken ct)
        {
            if (!await TableExistsAsync(connection, transaction, "Quests", ct))
                return;

            var hasObjectivesTable = await TableExistsAsync(connection, transaction, "QuestObjectives", ct);
            var hasQuestReqsTable = await TableExistsAsync(connection, transaction, "QuestRequirements", ct);
            var hasTraderReqsTable = await TableExistsAsync(connection, transaction, "QuestTraderRequirements", ct);
            var hasPoolReqsTable = await TableExistsAsync(connection, transaction, "QuestPoolRequirements", ct);
            var hasRequiredItemsTable = await TableExistsAsync(connection, transaction, "QuestRequiredItems", ct);
            var hasOptionalQuestsTable = await TableExistsAsync(connection, transaction, "OptionalQuests", ct);

            foreach (var qDiff in diff.QuestDiffs)
            {
                ct.ThrowIfCancellationRequested();

                if (qDiff.ChangeType == DiffChangeType.Added)
                {
                    var checkCmd = new SqliteCommand("SELECT COUNT(*) FROM Quests WHERE Id = @Id;", connection, transaction);
                    checkCmd.Parameters.AddWithValue("@Id", qDiff.QuestId);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct)) > 0;

                    if (!exists)
                    {
                        var insertSql = @"
                            INSERT INTO Quests (
                                Id, BsgId, Name, NameEN, NameKO, NameJA,
                                WikiPageLink, Trader, Location,
                                MinLevel, MinLevelApproved, MinLevelApprovedAt,
                                MinScavKarma, MinScavKarmaApproved, MinScavKarmaApprovedAt,
                                UpdatedAt, KappaRequired, Faction,
                                IsApproved, ApprovedAt,
                                RequiredEdition, RequiredEditionApproved, RequiredEditionApprovedAt,
                                ExcludedEdition, ExcludedEditionApproved, ExcludedEditionApprovedAt,
                                RequiredDecodeCount, RequiredDecodeCountApproved, RequiredDecodeCountApprovedAt,
                                RequiredPrestigeLevel, RequiredPrestigeLevelApproved, RequiredPrestigeLevelApprovedAt
                            ) VALUES (
                                @Id, @BsgId, @Name, @NameEN, @NameKO, @NameJA,
                                @WikiPageLink, @Trader, @Location,
                                @MinLevel, 0, NULL,
                                @MinScavKarma, 0, NULL,
                                @UpdatedAt, @KappaRequired, @Faction,
                                0, NULL,
                                @RequiredEdition, 0, NULL,
                                @ExcludedEdition, 0, NULL,
                                @RequiredDecodeCount, 0, NULL,
                                @RequiredPrestigeLevel, 0, NULL
                            );";

                        await using var insertCmd = new SqliteCommand(insertSql, connection, transaction);
                        insertCmd.Parameters.AddWithValue("@Id", qDiff.QuestId);
                        insertCmd.Parameters.AddWithValue("@BsgId", (object?)qDiff.BsgId ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@Name", qDiff.QuestName);
                        insertCmd.Parameters.AddWithValue("@NameEN", (object?)qDiff.RemoteQuest?.NameEN ?? (object?)qDiff.QuestName);
                        insertCmd.Parameters.AddWithValue("@NameKO", (object?)qDiff.RemoteQuest?.NameKO ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@NameJA", (object?)qDiff.RemoteQuest?.NameJA ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@WikiPageLink", (object?)qDiff.RemoteQuest?.WikiPageLink ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@Trader", (object?)qDiff.NewTrader ?? string.Empty);
                        insertCmd.Parameters.AddWithValue("@Location", (object?)qDiff.RemoteQuest?.Location ?? string.Empty);
                        insertCmd.Parameters.AddWithValue("@MinLevel", (object?)qDiff.NewMinLevel ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@MinScavKarma", (object?)qDiff.NewMinScavKarma ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        insertCmd.Parameters.AddWithValue("@KappaRequired", qDiff.NewKappaRequired == true ? 1 : 0);
                        insertCmd.Parameters.AddWithValue("@Faction", (object?)qDiff.NewFaction ?? string.Empty);
                        insertCmd.Parameters.AddWithValue("@RequiredEdition", (object?)qDiff.NewRequiredEdition ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@ExcludedEdition", (object?)qDiff.NewExcludedEdition ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@RequiredDecodeCount", (object?)qDiff.NewRequiredDecodeCount ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@RequiredPrestigeLevel", (object?)qDiff.NewRequiredPrestigeLevel ?? DBNull.Value);
                        await insertCmd.ExecuteNonQueryAsync(ct);

                        // Safe default objectives creation for new quests
                        if (hasObjectivesTable && qDiff.RemoteQuest?.Objectives != null && qDiff.RemoteQuest.Objectives.Count > 0)
                        {
                            for (int i = 0; i < qDiff.RemoteQuest.Objectives.Count; i++)
                            {
                                var objText = qDiff.RemoteQuest.Objectives[i];
                                if (string.IsNullOrWhiteSpace(objText)) continue;
                                if (objText.StartsWith("Survive and extract", StringComparison.OrdinalIgnoreCase) && KnownNonSurviveQuestIds.Contains(qDiff.QuestId))
                                {
                                    _log.Warning($"ApplyDiff: Rejecting fake survive objective for quest {qDiff.QuestId}");
                                    continue;
                                }

                                var objId = $"{qDiff.QuestId}_{i}";
                                var objSql = @"
                                    INSERT INTO QuestObjectives (
                                        Id, QuestId, SortOrder, ObjectiveType, Description,
                                        TargetType, TargetCount, ItemId, ItemName, RequiresFIR,
                                        MapName, LocationName, LocationPoints, Conditions, ContentHash,
                                        IsApproved, ApprovedAt, UpdatedAt, OptionalPoints, DogtagMinLevel, DogtagFaction
                                    ) VALUES (
                                        @Id, @QuestId, @SortOrder, 'Custom', @Description,
                                        NULL, NULL, NULL, NULL, 0,
                                        @MapName, NULL, NULL, NULL, NULL,
                                        0, NULL, @UpdatedAt, NULL, NULL, NULL
                                    );";

                                await using var objCmd = new SqliteCommand(objSql, connection, transaction);
                                objCmd.Parameters.AddWithValue("@Id", objId);
                                objCmd.Parameters.AddWithValue("@QuestId", qDiff.QuestId);
                                objCmd.Parameters.AddWithValue("@SortOrder", i);
                                objCmd.Parameters.AddWithValue("@Description", objText);
                                objCmd.Parameters.AddWithValue("@MapName", (object?)qDiff.RemoteQuest.Location ?? DBNull.Value);
                                objCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                                await objCmd.ExecuteNonQueryAsync(ct);
                            }
                        }
                    }
                }
                else if (qDiff.ChangeType == DiffChangeType.Modified)
                {
                    bool hasMinLevelChange = qDiff.PropertyChanges.Any(p => p.PropertyName == "MinLevel");
                    var updateSql = @"
                        UPDATE Quests SET
                            Name = CASE WHEN @Name IS NOT NULL THEN @Name ELSE Name END,
                            BsgId = CASE WHEN @BsgId IS NOT NULL THEN @BsgId ELSE BsgId END,
                            Trader = CASE WHEN @Trader IS NOT NULL THEN @Trader ELSE Trader END,
                            MinLevel = CASE WHEN @HasMinLevelChange = 1 THEN @MinLevel ELSE MinLevel END,
                            Faction = CASE WHEN @Faction IS NOT NULL THEN @Faction ELSE Faction END,
                            MinScavKarma = CASE WHEN @MinScavKarma IS NOT NULL THEN @MinScavKarma ELSE MinScavKarma END,
                            KappaRequired = CASE WHEN @KappaRequired IS NOT NULL THEN @KappaRequired ELSE KappaRequired END,
                            RequiredEdition = CASE WHEN @RequiredEdition IS NOT NULL THEN @RequiredEdition ELSE RequiredEdition END,
                            ExcludedEdition = CASE WHEN @ExcludedEdition IS NOT NULL THEN @ExcludedEdition ELSE ExcludedEdition END,
                            RequiredPrestigeLevel = CASE WHEN @RequiredPrestigeLevel IS NOT NULL THEN @RequiredPrestigeLevel ELSE RequiredPrestigeLevel END,
                            RequiredDecodeCount = CASE WHEN @RequiredDecodeCount IS NOT NULL THEN @RequiredDecodeCount ELSE RequiredDecodeCount END,
                            WikiPageLink = CASE WHEN @WikiPageLink IS NOT NULL THEN @WikiPageLink ELSE WikiPageLink END,
                            UpdatedAt = @UpdatedAt
                        WHERE Id = @Id;";

                    await using var updateCmd = new SqliteCommand(updateSql, connection, transaction);
                    updateCmd.Parameters.AddWithValue("@Id", qDiff.QuestId);
                    updateCmd.Parameters.AddWithValue("@Name", (object?)qDiff.QuestName ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@BsgId", (object?)qDiff.BsgId ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@Trader", (object?)qDiff.NewTrader ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@MinLevel", (object?)qDiff.NewMinLevel ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@HasMinLevelChange", hasMinLevelChange ? 1 : 0);
                    updateCmd.Parameters.AddWithValue("@Faction", (object?)qDiff.NewFaction ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@MinScavKarma", (object?)qDiff.NewMinScavKarma ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@KappaRequired", qDiff.NewKappaRequired.HasValue ? (qDiff.NewKappaRequired.Value ? 1 : 0) : DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@RequiredEdition", (object?)qDiff.NewRequiredEdition ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@ExcludedEdition", (object?)qDiff.NewExcludedEdition ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@RequiredPrestigeLevel", (object?)qDiff.NewRequiredPrestigeLevel ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@RequiredDecodeCount", (object?)qDiff.NewRequiredDecodeCount ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@WikiPageLink", (object?)qDiff.RemoteQuest?.WikiPageLink ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                    await updateCmd.ExecuteNonQueryAsync(ct);

                    if (hasObjectivesTable && qDiff.RemoteQuest?.Objectives != null && qDiff.RemoteQuest.Objectives.Count > 0)
                    {
                        var chkObjSql = "SELECT COUNT(*) FROM QuestObjectives WHERE QuestId = @QuestId;";
                        await using var chkObjCmd = new SqliteCommand(chkObjSql, connection, transaction);
                        chkObjCmd.Parameters.AddWithValue("@QuestId", qDiff.QuestId);
                        var objCount = Convert.ToInt32(await chkObjCmd.ExecuteScalarAsync(ct));
                        if (objCount == 0)
                        {
                            for (int i = 0; i < qDiff.RemoteQuest.Objectives.Count; i++)
                            {
                                var objText = qDiff.RemoteQuest.Objectives[i];
                                if (string.IsNullOrWhiteSpace(objText)) continue;
                                if (objText.StartsWith("Survive and extract", StringComparison.OrdinalIgnoreCase) && KnownNonSurviveQuestIds.Contains(qDiff.QuestId))
                                {
                                    _log.Warning($"ApplyDiff: Rejecting fake survive objective for quest {qDiff.QuestId}");
                                    continue;
                                }

                                var objId = $"{qDiff.QuestId}_{i}";
                                var objSql = @"
                                    INSERT INTO QuestObjectives (
                                        Id, QuestId, SortOrder, ObjectiveType, Description,
                                        TargetType, TargetCount, ItemId, ItemName, RequiresFIR,
                                        MapName, LocationName, LocationPoints, Conditions, ContentHash,
                                        IsApproved, ApprovedAt, UpdatedAt, OptionalPoints, DogtagMinLevel, DogtagFaction
                                    ) VALUES (
                                        @Id, @QuestId, @SortOrder, 'Custom', @Description,
                                        NULL, NULL, NULL, NULL, 0,
                                        @MapName, NULL, NULL, NULL, NULL,
                                        1, NULL, @UpdatedAt, NULL, NULL, NULL
                                    );";

                                await using var objCmd = new SqliteCommand(objSql, connection, transaction);
                                objCmd.Parameters.AddWithValue("@Id", objId);
                                objCmd.Parameters.AddWithValue("@QuestId", qDiff.QuestId);
                                objCmd.Parameters.AddWithValue("@SortOrder", i);
                                objCmd.Parameters.AddWithValue("@Description", objText);
                                objCmd.Parameters.AddWithValue("@MapName", (object?)qDiff.RemoteQuest.Location ?? DBNull.Value);
                                objCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                                await objCmd.ExecuteNonQueryAsync(ct);
                            }
                        }
                    }
                }
                else if (qDiff.ChangeType == DiffChangeType.Removed)
                {
                    // 1. Cascade delete from QuestRequirements (both as dependent and prerequisite)
                    if (hasQuestReqsTable)
                    {
                        var delReqSql = "DELETE FROM QuestRequirements WHERE QuestId = @Id OR RequiredQuestId = @Id;";
                        await using var cmd = new SqliteCommand(delReqSql, connection, transaction);
                        cmd.Parameters.AddWithValue("@Id", qDiff.QuestId);
                        await cmd.ExecuteNonQueryAsync(ct);
                    }

                    // 2. Cascade delete from QuestObjectives
                    if (hasObjectivesTable)
                    {
                        var delObjSql = "DELETE FROM QuestObjectives WHERE QuestId = @Id;";
                        await using var cmd = new SqliteCommand(delObjSql, connection, transaction);
                        cmd.Parameters.AddWithValue("@Id", qDiff.QuestId);
                        await cmd.ExecuteNonQueryAsync(ct);
                    }

                    // 3. Cascade delete from QuestTraderRequirements
                    if (hasTraderReqsTable)
                    {
                        var delTrSql = "DELETE FROM QuestTraderRequirements WHERE QuestId = @Id;";
                        await using var cmd = new SqliteCommand(delTrSql, connection, transaction);
                        cmd.Parameters.AddWithValue("@Id", qDiff.QuestId);
                        await cmd.ExecuteNonQueryAsync(ct);
                    }

                    // 4. Cascade delete from QuestPoolRequirements
                    if (hasPoolReqsTable)
                    {
                        var delPoolSql = "DELETE FROM QuestPoolRequirements WHERE QuestId = @Id;";
                        await using var cmd = new SqliteCommand(delPoolSql, connection, transaction);
                        cmd.Parameters.AddWithValue("@Id", qDiff.QuestId);
                        await cmd.ExecuteNonQueryAsync(ct);
                    }

                    // 5. Cascade delete from QuestRequiredItems
                    if (hasRequiredItemsTable)
                    {
                        var delItemSql = "DELETE FROM QuestRequiredItems WHERE QuestId = @Id;";
                        await using var cmd = new SqliteCommand(delItemSql, connection, transaction);
                        cmd.Parameters.AddWithValue("@Id", qDiff.QuestId);
                        await cmd.ExecuteNonQueryAsync(ct);
                    }

                    // 6. Cascade delete from OptionalQuests (both QuestId and AlternativeQuestId)
                    if (hasOptionalQuestsTable)
                    {
                        var delOptSql = "DELETE FROM OptionalQuests WHERE QuestId = @Id OR AlternativeQuestId = @Id;";
                        await using var cmd = new SqliteCommand(delOptSql, connection, transaction);
                        cmd.Parameters.AddWithValue("@Id", qDiff.QuestId);
                        await cmd.ExecuteNonQueryAsync(ct);
                    }

                    // 7. Finally delete the quest from Quests
                    var delSql = "DELETE FROM Quests WHERE Id = @Id;";
                    await using var delCmd = new SqliteCommand(delSql, connection, transaction);
                    delCmd.Parameters.AddWithValue("@Id", qDiff.QuestId);
                    await delCmd.ExecuteNonQueryAsync(ct);
                }

                // Guard against removed quests processing prerequisite insertions/deletions
                if (qDiff.ChangeType != DiffChangeType.Removed)
                {
                    // Handle direct prerequisite list deltas on QuestDiff
                    if (qDiff.RemovedPrerequisiteQuestIds != null && qDiff.RemovedPrerequisiteQuestIds.Count > 0)
                    {
                        foreach (var removedId in qDiff.RemovedPrerequisiteQuestIds)
                        {
                            var delReqSql = "DELETE FROM QuestRequirements WHERE QuestId = @QuestId AND RequiredQuestId = @ReqId;";
                            await using var delReqCmd = new SqliteCommand(delReqSql, connection, transaction);
                            delReqCmd.Parameters.AddWithValue("@QuestId", qDiff.QuestId);
                            delReqCmd.Parameters.AddWithValue("@ReqId", removedId);
                            await delReqCmd.ExecuteNonQueryAsync(ct);
                        }
                    }

                    if (qDiff.AddedPrerequisiteQuestIds != null && qDiff.AddedPrerequisiteQuestIds.Count > 0)
                    {
                        foreach (var addedId in qDiff.AddedPrerequisiteQuestIds)
                        {
                            if (isResilient)
                            {
                                // Verify prerequisite target and source exist in Quests before attempting insertion
                                var checkTargetSql = "SELECT (SELECT COUNT(*) FROM Quests WHERE Id = @QuestId) AS QExists, (SELECT COUNT(*) FROM Quests WHERE Id = @ReqId) AS ReqExists;";
                                await using var chkTargetCmd = new SqliteCommand(checkTargetSql, connection, transaction);
                                chkTargetCmd.Parameters.AddWithValue("@QuestId", qDiff.QuestId);
                                chkTargetCmd.Parameters.AddWithValue("@ReqId", addedId);
                                await using var targetReader = await chkTargetCmd.ExecuteReaderAsync(ct);
                                bool qExists = false;
                                bool reqExists = false;
                                if (await targetReader.ReadAsync(ct))
                                {
                                    qExists = targetReader.GetInt32(0) > 0;
                                    reqExists = targetReader.GetInt32(1) > 0;
                                }
                                await targetReader.CloseAsync();

                                if (!qExists || !reqExists)
                                {
                                    _log.Warning($"ApplyQuests: Skipping prerequisite link '{qDiff.QuestId}' -> '{addedId}' because target or required quest does not exist in Quests table (qExists={qExists}, reqExists={reqExists}).");
                                    continue;
                                }
                            }

                            var checkSql = "SELECT COUNT(*) FROM QuestRequirements WHERE QuestId = @QuestId AND RequiredQuestId = @ReqId;";
                            await using var checkCmd = new SqliteCommand(checkSql, connection, transaction);
                            checkCmd.Parameters.AddWithValue("@QuestId", qDiff.QuestId);
                            checkCmd.Parameters.AddWithValue("@ReqId", addedId);
                            var existsReq = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct)) > 0;

                            if (!existsReq)
                            {
                                try
                                {
                                    var reqId = $"{qDiff.QuestId}_{addedId}";
                                    var insReqSql = @"
                                        INSERT INTO QuestRequirements (
                                            Id, QuestId, RequiredQuestId, RequirementType, DelayMinutes, GroupId, IsApproved, UpdatedAt
                                        ) VALUES (
                                            @Id, @QuestId, @ReqId, 'Complete', 0, 0, 0, @UpdatedAt
                                        );";
                                    await using var insReqCmd = new SqliteCommand(insReqSql, connection, transaction);
                                    insReqCmd.Parameters.AddWithValue("@Id", reqId);
                                    insReqCmd.Parameters.AddWithValue("@QuestId", qDiff.QuestId);
                                    insReqCmd.Parameters.AddWithValue("@ReqId", addedId);
                                    insReqCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                                    await insReqCmd.ExecuteNonQueryAsync(ct);
                                }
                                catch (SqliteException ex) when (isResilient && ex.SqliteErrorCode == 19)
                                {
                                    _log.Warning($"ApplyQuests: Foreign key constraint failure (SQLite error 19) for prerequisite '{qDiff.QuestId}' -> '{addedId}': {ex.Message}. Skipping requirement.");
                                }
                            }
                        }
                    }
                }
            }
        }

        private async Task ApplyQuestRequirementsAsync(SqliteConnection connection, SqliteTransaction transaction, DataDiffReport diff, bool isResilient, CancellationToken ct)
        {
            if (!await TableExistsAsync(connection, transaction, "QuestRequirements", ct))
                return;

            foreach (var reqDiff in diff.QuestRequirementDiffs)
            {
                ct.ThrowIfCancellationRequested();

                var reqId = string.IsNullOrWhiteSpace(reqDiff.RequirementId)
                    ? $"{reqDiff.QuestId}_{reqDiff.RequiredQuestId}"
                    : reqDiff.RequirementId;

                if (reqDiff.ChangeType == DiffChangeType.Added)
                {
                    if (isResilient)
                    {
                        // Verify prerequisite target and source exist in Quests before attempting insertion
                        var checkTargetSql = "SELECT (SELECT COUNT(*) FROM Quests WHERE Id = @QuestId) AS QExists, (SELECT COUNT(*) FROM Quests WHERE Id = @RequiredQuestId) AS ReqExists;";
                        await using var chkTargetCmd = new SqliteCommand(checkTargetSql, connection, transaction);
                        chkTargetCmd.Parameters.AddWithValue("@QuestId", reqDiff.QuestId);
                        chkTargetCmd.Parameters.AddWithValue("@RequiredQuestId", reqDiff.RequiredQuestId);
                        await using var targetReader = await chkTargetCmd.ExecuteReaderAsync(ct);
                        bool qExists = false;
                        bool reqExists = false;
                        if (await targetReader.ReadAsync(ct))
                        {
                            qExists = targetReader.GetInt32(0) > 0;
                            reqExists = targetReader.GetInt32(1) > 0;
                        }
                        await targetReader.CloseAsync();

                        if (!qExists || !reqExists)
                        {
                            _log.Warning($"ApplyQuestRequirements: Skipping Added prerequisite '{reqDiff.QuestId}' -> '{reqDiff.RequiredQuestId}' because QuestId or RequiredQuestId does not exist in Quests table (qExists={qExists}, reqExists={reqExists}).");
                            continue;
                        }
                    }

                    var chkSql = "SELECT COUNT(*) FROM QuestRequirements WHERE Id = @Id OR (QuestId = @QuestId AND RequiredQuestId = @RequiredQuestId);";
                    await using var chkCmd = new SqliteCommand(chkSql, connection, transaction);
                    chkCmd.Parameters.AddWithValue("@Id", reqId);
                    chkCmd.Parameters.AddWithValue("@QuestId", reqDiff.QuestId);
                    chkCmd.Parameters.AddWithValue("@RequiredQuestId", reqDiff.RequiredQuestId);
                    var exists = Convert.ToInt32(await chkCmd.ExecuteScalarAsync(ct)) > 0;

                    try
                    {
                        if (!exists)
                        {
                            var insSql = @"
                                INSERT INTO QuestRequirements (
                                    Id, QuestId, RequiredQuestId, RequirementType, DelayMinutes, GroupId, IsApproved, UpdatedAt
                                ) VALUES (
                                    @Id, @QuestId, @RequiredQuestId, @RequirementType, @DelayMinutes, @GroupId, 0, @UpdatedAt
                                );";
                            await using var insCmd = new SqliteCommand(insSql, connection, transaction);
                            insCmd.Parameters.AddWithValue("@Id", reqId);
                            insCmd.Parameters.AddWithValue("@QuestId", reqDiff.QuestId);
                            insCmd.Parameters.AddWithValue("@RequiredQuestId", reqDiff.RequiredQuestId);
                            insCmd.Parameters.AddWithValue("@RequirementType", reqDiff.NewRequirementType ?? "Complete");
                            insCmd.Parameters.AddWithValue("@DelayMinutes", (object?)reqDiff.NewDelayMinutes ?? DBNull.Value);
                            insCmd.Parameters.AddWithValue("@GroupId", reqDiff.NewGroupId ?? 0);
                            insCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                            await insCmd.ExecuteNonQueryAsync(ct);
                        }
                        else
                        {
                            var upSql = @"
                                UPDATE QuestRequirements SET
                                    RequirementType = @RequirementType,
                                    DelayMinutes = CASE WHEN @DelayMinutes IS NOT NULL THEN @DelayMinutes ELSE DelayMinutes END,
                                    GroupId = @GroupId,
                                    UpdatedAt = @UpdatedAt
                                WHERE Id = @Id OR (QuestId = @QuestId AND RequiredQuestId = @RequiredQuestId);";
                            await using var upCmd = new SqliteCommand(upSql, connection, transaction);
                            upCmd.Parameters.AddWithValue("@Id", reqId);
                            upCmd.Parameters.AddWithValue("@QuestId", reqDiff.QuestId);
                            upCmd.Parameters.AddWithValue("@RequiredQuestId", reqDiff.RequiredQuestId);
                            upCmd.Parameters.AddWithValue("@RequirementType", reqDiff.NewRequirementType ?? "Complete");
                            upCmd.Parameters.AddWithValue("@DelayMinutes", (object?)reqDiff.NewDelayMinutes ?? DBNull.Value);
                            upCmd.Parameters.AddWithValue("@GroupId", reqDiff.NewGroupId ?? 0);
                            upCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                            await upCmd.ExecuteNonQueryAsync(ct);
                        }
                    }
                    catch (SqliteException ex) when (isResilient && ex.SqliteErrorCode == 19)
                    {
                        _log.Warning($"ApplyQuestRequirements: Foreign key constraint failure (SQLite error 19) on Added prerequisite '{reqDiff.QuestId}' -> '{reqDiff.RequiredQuestId}': {ex.Message}. Skipping requirement.");
                    }
                }
                else if (reqDiff.ChangeType == DiffChangeType.Modified)
                {
                    if (isResilient)
                    {
                        // Verify prerequisite target and source exist in Quests before attempting update
                        var checkTargetSql = "SELECT (SELECT COUNT(*) FROM Quests WHERE Id = @QuestId) AS QExists, (SELECT COUNT(*) FROM Quests WHERE Id = @RequiredQuestId) AS ReqExists;";
                        await using var chkTargetCmd = new SqliteCommand(checkTargetSql, connection, transaction);
                        chkTargetCmd.Parameters.AddWithValue("@QuestId", reqDiff.QuestId);
                        chkTargetCmd.Parameters.AddWithValue("@RequiredQuestId", reqDiff.RequiredQuestId);
                        await using var targetReader = await chkTargetCmd.ExecuteReaderAsync(ct);
                        bool qExists = false;
                        bool reqExists = false;
                        if (await targetReader.ReadAsync(ct))
                        {
                            qExists = targetReader.GetInt32(0) > 0;
                            reqExists = targetReader.GetInt32(1) > 0;
                        }
                        await targetReader.CloseAsync();

                        if (!qExists || !reqExists)
                        {
                            _log.Warning($"ApplyQuestRequirements: Skipping Modified prerequisite '{reqDiff.QuestId}' -> '{reqDiff.RequiredQuestId}' because QuestId or RequiredQuestId does not exist in Quests table (qExists={qExists}, reqExists={reqExists}).");
                            continue;
                        }
                    }

                    try
                    {
                        var upSql = @"
                            UPDATE QuestRequirements SET
                                RequirementType = CASE WHEN @RequirementType IS NOT NULL THEN @RequirementType ELSE RequirementType END,
                                DelayMinutes = CASE WHEN @DelayMinutes IS NOT NULL THEN @DelayMinutes ELSE DelayMinutes END,
                                GroupId = CASE WHEN @GroupId IS NOT NULL THEN @GroupId ELSE GroupId END,
                                UpdatedAt = @UpdatedAt
                            WHERE Id = @Id OR (QuestId = @QuestId AND RequiredQuestId = @RequiredQuestId);";
                        await using var upCmd = new SqliteCommand(upSql, connection, transaction);
                        upCmd.Parameters.AddWithValue("@Id", reqId);
                        upCmd.Parameters.AddWithValue("@QuestId", reqDiff.QuestId);
                        upCmd.Parameters.AddWithValue("@RequiredQuestId", reqDiff.RequiredQuestId);
                        upCmd.Parameters.AddWithValue("@RequirementType", (object?)reqDiff.NewRequirementType ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@DelayMinutes", (object?)reqDiff.NewDelayMinutes ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@GroupId", (object?)reqDiff.NewGroupId ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await upCmd.ExecuteNonQueryAsync(ct);
                    }
                    catch (SqliteException ex) when (isResilient && ex.SqliteErrorCode == 19)
                    {
                        _log.Warning($"ApplyQuestRequirements: Foreign key constraint failure (SQLite error 19) on Modified prerequisite '{reqDiff.QuestId}' -> '{reqDiff.RequiredQuestId}': {ex.Message}. Skipping requirement.");
                    }
                }
                else if (reqDiff.ChangeType == DiffChangeType.Removed)
                {
                    var delSql = "DELETE FROM QuestRequirements WHERE Id = @Id OR (QuestId = @QuestId AND RequiredQuestId = @RequiredQuestId);";
                    await using var delCmd = new SqliteCommand(delSql, connection, transaction);
                    delCmd.Parameters.AddWithValue("@Id", reqId);
                    delCmd.Parameters.AddWithValue("@QuestId", reqDiff.QuestId);
                    delCmd.Parameters.AddWithValue("@RequiredQuestId", reqDiff.RequiredQuestId);
                    await delCmd.ExecuteNonQueryAsync(ct);
                }
            }
        }

        private async Task ApplyQuestRequiredItemsAsync(SqliteConnection connection, SqliteTransaction transaction, DataDiffReport diff, CancellationToken ct)
        {
            if (!await TableExistsAsync(connection, transaction, "QuestRequiredItems", ct))
                return;

            var hasItemsTable = await TableExistsAsync(connection, transaction, "Items", ct);

            foreach (var itemDiff in diff.QuestItemDiffs)
            {
                ct.ThrowIfCancellationRequested();

                // Guard against corrupt manifests in Reserve Expert (historical TarkovTracker scraping bug)
                if (string.Equals(itemDiff.QuestId, "608974af4b05530f55550c21", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(itemDiff.ItemName) &&
                    itemDiff.ItemName.Contains("manifest", StringComparison.OrdinalIgnoreCase))
                {
                    _log.Warning($"ApplyDiff: Rejecting corrupt cargo manifest '{itemDiff.ItemName}' for Reserve Expert ({itemDiff.QuestId}).");
                    continue;
                }


                string? validatedItemId = null;
                if (!string.IsNullOrEmpty(itemDiff.ItemId) && hasItemsTable)
                {
                    var itemCheckSql = "SELECT Id FROM Items WHERE Id = @id OR BsgId = @id LIMIT 1;";
                    await using var itemChkCmd = new SqliteCommand(itemCheckSql, connection, transaction);
                    itemChkCmd.Parameters.AddWithValue("@id", itemDiff.ItemId);
                    var matched = (string?)await itemChkCmd.ExecuteScalarAsync(ct);
                    validatedItemId = matched;
                }

                var rId = string.IsNullOrWhiteSpace(itemDiff.RequirementId)
                    ? Guid.NewGuid().ToString("N")
                    : itemDiff.RequirementId;

                if (itemDiff.ChangeType == DiffChangeType.Added)
                {
                    var chkSql = "SELECT COUNT(*) FROM QuestRequiredItems WHERE Id = @Id OR (QuestId = @QuestId AND ItemName = @ItemName);";
                    await using var chkCmd = new SqliteCommand(chkSql, connection, transaction);
                    chkCmd.Parameters.AddWithValue("@Id", rId);
                    chkCmd.Parameters.AddWithValue("@QuestId", itemDiff.QuestId);
                    chkCmd.Parameters.AddWithValue("@ItemName", itemDiff.ItemName);
                    var exists = Convert.ToInt32(await chkCmd.ExecuteScalarAsync(ct)) > 0;

                    if (!exists)
                    {
                        var insSql = @"
                            INSERT INTO QuestRequiredItems (
                                Id, QuestId, ItemId, ItemName, Count, RequiresFIR, RequirementType, SortOrder, IsApproved, UpdatedAt
                            ) VALUES (
                                @Id, @QuestId, @ItemId, @ItemName, @Count, @RequiresFIR, @RequirementType, 0, 0, @UpdatedAt
                            );";
                        await using var insCmd = new SqliteCommand(insSql, connection, transaction);
                        insCmd.Parameters.AddWithValue("@Id", rId);
                        insCmd.Parameters.AddWithValue("@QuestId", itemDiff.QuestId);
                        insCmd.Parameters.AddWithValue("@ItemId", (object?)validatedItemId ?? DBNull.Value);
                        insCmd.Parameters.AddWithValue("@ItemName", itemDiff.ItemName);
                        insCmd.Parameters.AddWithValue("@Count", itemDiff.NewQuantity ?? 1);
                        insCmd.Parameters.AddWithValue("@RequiresFIR", itemDiff.NewRequiresFIR == true ? 1 : 0);
                        insCmd.Parameters.AddWithValue("@RequirementType", itemDiff.NewRequirementType ?? "Required");
                        insCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await insCmd.ExecuteNonQueryAsync(ct);
                    }
                    else
                    {
                        var upSql = @"
                            UPDATE QuestRequiredItems SET
                                Count = @Count,
                                RequiresFIR = @RequiresFIR,
                                RequirementType = CASE WHEN @RequirementType IS NOT NULL THEN @RequirementType ELSE RequirementType END,
                                ItemId = CASE WHEN @ItemId IS NOT NULL THEN @ItemId ELSE ItemId END,
                                UpdatedAt = @UpdatedAt
                            WHERE Id = @Id OR (QuestId = @QuestId AND ItemName = @ItemName);";
                        await using var upCmd = new SqliteCommand(upSql, connection, transaction);
                        upCmd.Parameters.AddWithValue("@Id", rId);
                        upCmd.Parameters.AddWithValue("@QuestId", itemDiff.QuestId);
                        upCmd.Parameters.AddWithValue("@ItemName", itemDiff.ItemName);
                        upCmd.Parameters.AddWithValue("@ItemId", (object?)validatedItemId ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@Count", itemDiff.NewQuantity ?? 1);
                        upCmd.Parameters.AddWithValue("@RequiresFIR", itemDiff.NewRequiresFIR == true ? 1 : 0);
                        upCmd.Parameters.AddWithValue("@RequirementType", (object?)itemDiff.NewRequirementType ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await upCmd.ExecuteNonQueryAsync(ct);
                    }
                }
                else if (itemDiff.ChangeType == DiffChangeType.Modified)
                {
                    var upSql = @"
                        UPDATE QuestRequiredItems SET
                            Count = CASE WHEN @Count IS NOT NULL THEN @Count ELSE Count END,
                            RequiresFIR = CASE WHEN @RequiresFIR IS NOT NULL THEN @RequiresFIR ELSE RequiresFIR END,
                            RequirementType = CASE WHEN @RequirementType IS NOT NULL THEN @RequirementType ELSE RequirementType END,
                            ItemId = CASE WHEN @ItemId IS NOT NULL THEN @ItemId ELSE ItemId END,
                            UpdatedAt = @UpdatedAt
                        WHERE Id = @Id OR (QuestId = @QuestId AND ItemName = @ItemName);";
                    await using var upCmd = new SqliteCommand(upSql, connection, transaction);
                    upCmd.Parameters.AddWithValue("@Id", rId);
                    upCmd.Parameters.AddWithValue("@QuestId", itemDiff.QuestId);
                    upCmd.Parameters.AddWithValue("@ItemName", itemDiff.ItemName);
                    upCmd.Parameters.AddWithValue("@ItemId", (object?)validatedItemId ?? DBNull.Value);
                    upCmd.Parameters.AddWithValue("@Count", (object?)itemDiff.NewQuantity ?? DBNull.Value);
                    upCmd.Parameters.AddWithValue("@RequiresFIR", itemDiff.NewRequiresFIR.HasValue ? (itemDiff.NewRequiresFIR.Value ? 1 : 0) : DBNull.Value);
                    upCmd.Parameters.AddWithValue("@RequirementType", (object?)itemDiff.NewRequirementType ?? DBNull.Value);
                    upCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                    await upCmd.ExecuteNonQueryAsync(ct);
                }
                else if (itemDiff.ChangeType == DiffChangeType.Removed)
                {
                    var delSql = "DELETE FROM QuestRequiredItems WHERE Id = @Id OR (QuestId = @QuestId AND ItemName = @ItemName);";
                    await using var delCmd = new SqliteCommand(delSql, connection, transaction);
                    delCmd.Parameters.AddWithValue("@Id", rId);
                    delCmd.Parameters.AddWithValue("@QuestId", itemDiff.QuestId);
                    delCmd.Parameters.AddWithValue("@ItemName", itemDiff.ItemName);
                    await delCmd.ExecuteNonQueryAsync(ct);
                }
            }
        }

        private async Task ApplyQuestTraderRequirementsAsync(SqliteConnection connection, SqliteTransaction transaction, DataDiffReport diff, bool isResilient, CancellationToken ct)
        {
            if (!await TableExistsAsync(connection, transaction, "QuestTraderRequirements", ct))
                return;

            var validQuestCheckCmd = new SqliteCommand("SELECT COUNT(*) FROM Quests WHERE Id = @id;", connection, transaction);
            validQuestCheckCmd.Parameters.Add("@id", SqliteType.Text);

            foreach (var trDiff in diff.QuestTraderRequirementDiffs)
            {
                ct.ThrowIfCancellationRequested();

                validQuestCheckCmd.Parameters["@id"].Value = trDiff.QuestId;
                var questExists = Convert.ToInt32(await validQuestCheckCmd.ExecuteScalarAsync(ct)) > 0;
                if (!questExists)
                {
                    if (isResilient)
                    {
                        _log.Warning($"ApplyQuestTraderRequirements: Quest '{trDiff.QuestId}' does not exist in Quests table. Skipping requirement '{trDiff.RequirementId}'.");
                        continue;
                    }
                }

                var rId = string.IsNullOrWhiteSpace(trDiff.RequirementId)
                    ? Guid.NewGuid().ToString("N")
                    : trDiff.RequirementId;

                if (trDiff.ChangeType == DiffChangeType.Added)
                {
                    try
                    {
                        var insSql = @"
                            INSERT OR REPLACE INTO QuestTraderRequirements (
                                Id, QuestId, TraderId, TraderName, RequirementType, CompareMethod, RequiredLevel, SortOrder, IsApproved, UpdatedAt
                            ) VALUES (
                                @Id, @QuestId, @TraderId, @TraderName, @RequirementType, @CompareMethod, @RequiredLevel, @SortOrder, 1, @UpdatedAt
                            );";
                        await using var insCmd = new SqliteCommand(insSql, connection, transaction);
                        insCmd.Parameters.AddWithValue("@Id", rId);
                        insCmd.Parameters.AddWithValue("@QuestId", trDiff.QuestId);
                        insCmd.Parameters.AddWithValue("@TraderId", (object?)trDiff.TraderId ?? DBNull.Value);
                        insCmd.Parameters.AddWithValue("@TraderName", trDiff.TraderName);
                        insCmd.Parameters.AddWithValue("@RequirementType", (object?)trDiff.RemoteRequirement?.RequirementType ?? "Level");
                        insCmd.Parameters.AddWithValue("@CompareMethod", trDiff.NewCompareMethod ?? ">=");
                        insCmd.Parameters.AddWithValue("@RequiredLevel", trDiff.NewRequiredLevel ?? 1);
                        insCmd.Parameters.AddWithValue("@SortOrder", trDiff.RemoteRequirement?.SortOrder ?? 0);
                        insCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await insCmd.ExecuteNonQueryAsync(ct);
                    }
                    catch (SqliteException ex) when (isResilient && ex.SqliteErrorCode == 19)
                    {
                        _log.Warning($"ApplyQuestTraderRequirements: Foreign key constraint failure (SQLite error 19) on Added requirement for quest '{trDiff.QuestId}': {ex.Message}. Skipping requirement.");
                    }
                }
                else if (trDiff.ChangeType == DiffChangeType.Modified)
                {
                    try
                    {
                        var upSql = @"
                            UPDATE QuestTraderRequirements SET
                                RequiredLevel = CASE WHEN @RequiredLevel IS NOT NULL THEN @RequiredLevel ELSE RequiredLevel END,
                                CompareMethod = CASE WHEN @CompareMethod IS NOT NULL THEN @CompareMethod ELSE CompareMethod END,
                                TraderId = CASE WHEN @TraderId IS NOT NULL THEN @TraderId ELSE TraderId END,
                                UpdatedAt = @UpdatedAt
                            WHERE Id = @Id OR (QuestId = @QuestId AND TraderName = @TraderName);";
                        await using var upCmd = new SqliteCommand(upSql, connection, transaction);
                        upCmd.Parameters.AddWithValue("@Id", rId);
                        upCmd.Parameters.AddWithValue("@QuestId", trDiff.QuestId);
                        upCmd.Parameters.AddWithValue("@TraderName", trDiff.TraderName);
                        upCmd.Parameters.AddWithValue("@TraderId", (object?)trDiff.TraderId ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@RequiredLevel", (object?)trDiff.NewRequiredLevel ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@CompareMethod", (object?)trDiff.NewCompareMethod ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await upCmd.ExecuteNonQueryAsync(ct);
                    }
                    catch (SqliteException ex) when (isResilient && ex.SqliteErrorCode == 19)
                    {
                        _log.Warning($"ApplyQuestTraderRequirements: Foreign key constraint failure (SQLite error 19) on Modified requirement for quest '{trDiff.QuestId}': {ex.Message}. Skipping requirement.");
                    }
                }
                else if (trDiff.ChangeType == DiffChangeType.Removed)
                {
                    var delSql = string.IsNullOrWhiteSpace(rId)
                        ? "DELETE FROM QuestTraderRequirements WHERE QuestId = @QuestId AND TraderName = @TraderName;"
                        : "DELETE FROM QuestTraderRequirements WHERE Id = @Id;";
                    await using var delCmd = new SqliteCommand(delSql, connection, transaction);
                    delCmd.Parameters.AddWithValue("@Id", rId);
                    delCmd.Parameters.AddWithValue("@QuestId", trDiff.QuestId);
                    delCmd.Parameters.AddWithValue("@TraderName", trDiff.TraderName);
                    await delCmd.ExecuteNonQueryAsync(ct);
                }
            }
        }

        private async Task ApplyQuestPoolRequirementsAsync(SqliteConnection connection, SqliteTransaction transaction, DataDiffReport diff, bool isResilient, CancellationToken ct)
        {
            if (!await TableExistsAsync(connection, transaction, "QuestPoolRequirements", ct))
                return;

            var validQuestCheckCmd = new SqliteCommand("SELECT COUNT(*) FROM Quests WHERE Id = @id;", connection, transaction);
            validQuestCheckCmd.Parameters.Add("@id", SqliteType.Text);

            foreach (var poolDiff in diff.QuestPoolRequirementDiffs)
            {
                ct.ThrowIfCancellationRequested();

                validQuestCheckCmd.Parameters["@id"].Value = poolDiff.QuestId;
                var questExists = Convert.ToInt32(await validQuestCheckCmd.ExecuteScalarAsync(ct)) > 0;
                if (!questExists)
                {
                    if (isResilient)
                    {
                        _log.Warning($"ApplyQuestPoolRequirements: Quest '{poolDiff.QuestId}' does not exist in Quests table. Skipping requirement '{poolDiff.RequirementId}'.");
                        continue;
                    }
                }

                var rId = string.IsNullOrWhiteSpace(poolDiff.RequirementId)
                    ? Guid.NewGuid().ToString("N")
                    : poolDiff.RequirementId;

                if (poolDiff.ChangeType == DiffChangeType.Added)
                {
                    try
                    {
                        var insSql = @"
                            INSERT OR REPLACE INTO QuestPoolRequirements (
                                Id, QuestId, PoolTrader, PoolTraderLL, RequiredCompletedCount, Description, IsApproved, UpdatedAt
                            ) VALUES (
                                @Id, @QuestId, @PoolTrader, @PoolTraderLL, @RequiredCompletedCount, @Description, 1, @UpdatedAt
                            );";
                        await using var insCmd = new SqliteCommand(insSql, connection, transaction);
                        insCmd.Parameters.AddWithValue("@Id", rId);
                        insCmd.Parameters.AddWithValue("@QuestId", poolDiff.QuestId);
                        insCmd.Parameters.AddWithValue("@PoolTrader", poolDiff.PoolTrader);
                        insCmd.Parameters.AddWithValue("@PoolTraderLL", (object?)poolDiff.PoolTraderLL ?? DBNull.Value);
                        insCmd.Parameters.AddWithValue("@RequiredCompletedCount", poolDiff.NewRequiredCompletedCount ?? 1);
                        insCmd.Parameters.AddWithValue("@Description", (object?)poolDiff.NewDescription ?? (object?)poolDiff.RemoteRequirement?.Description ?? DBNull.Value);
                        insCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await insCmd.ExecuteNonQueryAsync(ct);
                    }
                    catch (SqliteException ex) when (isResilient && ex.SqliteErrorCode == 19)
                    {
                        _log.Warning($"ApplyQuestPoolRequirements: Foreign key constraint failure (SQLite error 19) on Added pool requirement for quest '{poolDiff.QuestId}': {ex.Message}. Skipping requirement.");
                    }
                }
                else if (poolDiff.ChangeType == DiffChangeType.Modified)
                {
                    try
                    {
                        var upSql = @"
                            UPDATE QuestPoolRequirements SET
                                RequiredCompletedCount = CASE WHEN @RequiredCompletedCount IS NOT NULL THEN @RequiredCompletedCount ELSE RequiredCompletedCount END,
                                Description = CASE WHEN @Description IS NOT NULL THEN @Description ELSE Description END,
                                UpdatedAt = @UpdatedAt
                            WHERE Id = @Id OR (QuestId = @QuestId AND PoolTrader = @PoolTrader AND (PoolTraderLL = @PoolTraderLL OR (@PoolTraderLL IS NULL AND PoolTraderLL IS NULL)));";
                        await using var upCmd = new SqliteCommand(upSql, connection, transaction);
                        upCmd.Parameters.AddWithValue("@Id", rId);
                        upCmd.Parameters.AddWithValue("@QuestId", poolDiff.QuestId);
                        upCmd.Parameters.AddWithValue("@PoolTrader", poolDiff.PoolTrader);
                        upCmd.Parameters.AddWithValue("@PoolTraderLL", (object?)poolDiff.PoolTraderLL ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@RequiredCompletedCount", (object?)poolDiff.NewRequiredCompletedCount ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@Description", (object?)poolDiff.NewDescription ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await upCmd.ExecuteNonQueryAsync(ct);
                    }
                    catch (SqliteException ex) when (isResilient && ex.SqliteErrorCode == 19)
                    {
                        _log.Warning($"ApplyQuestPoolRequirements: Foreign key constraint failure (SQLite error 19) on Modified pool requirement for quest '{poolDiff.QuestId}': {ex.Message}. Skipping requirement.");
                    }
                }
                else if (poolDiff.ChangeType == DiffChangeType.Removed)
                {
                    var delSql = string.IsNullOrWhiteSpace(rId)
                        ? "DELETE FROM QuestPoolRequirements WHERE QuestId = @QuestId AND PoolTrader = @PoolTrader;"
                        : "DELETE FROM QuestPoolRequirements WHERE Id = @Id;";
                    await using var delCmd = new SqliteCommand(delSql, connection, transaction);
                    delCmd.Parameters.AddWithValue("@Id", rId);
                    delCmd.Parameters.AddWithValue("@QuestId", poolDiff.QuestId);
                    delCmd.Parameters.AddWithValue("@PoolTrader", poolDiff.PoolTrader);
                    await delCmd.ExecuteNonQueryAsync(ct);
                }
            }
        }

        private async Task ApplyHideoutStationsAsync(SqliteConnection connection, SqliteTransaction transaction, DataDiffReport diff, CancellationToken ct)
        {
            if (!await TableExistsAsync(connection, transaction, "HideoutStations", ct))
                return;

            foreach (var sDiff in diff.HideoutStationDiffs)
            {
                ct.ThrowIfCancellationRequested();

                if (sDiff.ChangeType == DiffChangeType.Added)
                {
                    var checkCmd = new SqliteCommand("SELECT COUNT(*) FROM HideoutStations WHERE Id = @Id;", connection, transaction);
                    checkCmd.Parameters.AddWithValue("@Id", sDiff.StationId);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct)) > 0;

                    if (!exists)
                    {
                        var insSql = @"
                            INSERT INTO HideoutStations (
                                Id, Name, NameKO, NameJA, NormalizedName, ImageLink, MaxLevel, UpdatedAt
                            ) VALUES (
                                @Id, @Name, @NameKO, @NameJA, @NormalizedName, @ImageLink, @MaxLevel, @UpdatedAt
                            );";
                        await using var insCmd = new SqliteCommand(insSql, connection, transaction);
                        insCmd.Parameters.AddWithValue("@Id", sDiff.StationId);
                        insCmd.Parameters.AddWithValue("@Name", sDiff.StationName);
                        insCmd.Parameters.AddWithValue("@NameKO", (object?)sDiff.RemoteStation?.NameKO ?? DBNull.Value);
                        insCmd.Parameters.AddWithValue("@NameJA", (object?)sDiff.RemoteStation?.NameJA ?? DBNull.Value);
                        insCmd.Parameters.AddWithValue("@NormalizedName", (object?)sDiff.RemoteStation?.NormalizedName ?? DBNull.Value);
                        insCmd.Parameters.AddWithValue("@ImageLink", (object?)sDiff.RemoteStation?.ImageLink ?? DBNull.Value);
                        insCmd.Parameters.AddWithValue("@MaxLevel", sDiff.NewMaxLevel ?? 0);
                        insCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await insCmd.ExecuteNonQueryAsync(ct);
                    }
                    else
                    {
                        var upSql = @"
                            UPDATE HideoutStations SET
                                MaxLevel = CASE WHEN @MaxLevel IS NOT NULL THEN @MaxLevel ELSE MaxLevel END,
                                UpdatedAt = @UpdatedAt
                            WHERE Id = @Id;";
                        await using var upCmd = new SqliteCommand(upSql, connection, transaction);
                        upCmd.Parameters.AddWithValue("@Id", sDiff.StationId);
                        upCmd.Parameters.AddWithValue("@MaxLevel", (object?)sDiff.NewMaxLevel ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await upCmd.ExecuteNonQueryAsync(ct);
                    }
                }
                else if (sDiff.ChangeType == DiffChangeType.Modified)
                {
                    var upSql = @"
                        UPDATE HideoutStations SET
                            MaxLevel = CASE WHEN @MaxLevel IS NOT NULL THEN @MaxLevel ELSE MaxLevel END,
                            UpdatedAt = @UpdatedAt
                        WHERE Id = @Id;";
                    await using var upCmd = new SqliteCommand(upSql, connection, transaction);
                    upCmd.Parameters.AddWithValue("@Id", sDiff.StationId);
                    upCmd.Parameters.AddWithValue("@MaxLevel", (object?)sDiff.NewMaxLevel ?? DBNull.Value);
                    upCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                    await upCmd.ExecuteNonQueryAsync(ct);
                }
                else if (sDiff.ChangeType == DiffChangeType.Removed)
                {
                    var delSql = "DELETE FROM HideoutStations WHERE Id = @Id;";
                    await using var delCmd = new SqliteCommand(delSql, connection, transaction);
                    delCmd.Parameters.AddWithValue("@Id", sDiff.StationId);
                    await delCmd.ExecuteNonQueryAsync(ct);
                }
            }
        }

        private async Task ApplyHideoutLevelsAsync(SqliteConnection connection, SqliteTransaction transaction, DataDiffReport diff, CancellationToken ct)
        {
            if (!await TableExistsAsync(connection, transaction, "HideoutLevels", ct))
                return;

            foreach (var lDiff in diff.HideoutLevelDiffs)
            {
                ct.ThrowIfCancellationRequested();

                var lvlId = string.IsNullOrWhiteSpace(lDiff.LevelId) ? $"{lDiff.StationId}_{lDiff.Level}" : lDiff.LevelId;


                if (lDiff.ChangeType == DiffChangeType.Added)
                {
                    var checkCmd = new SqliteCommand("SELECT COUNT(*) FROM HideoutLevels WHERE Id = @Id OR (StationId = @StationId AND Level = @Level);", connection, transaction);
                    checkCmd.Parameters.AddWithValue("@Id", lvlId);
                    checkCmd.Parameters.AddWithValue("@StationId", lDiff.StationId);
                    checkCmd.Parameters.AddWithValue("@Level", lDiff.Level);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct)) > 0;

                    if (!exists)
                    {
                        var insSql = @"
                            INSERT INTO HideoutLevels (
                                Id, StationId, Level, ConstructionTime, UpdatedAt
                            ) VALUES (
                                @Id, @StationId, @Level, @ConstructionTime, @UpdatedAt
                            );";
                        await using var insCmd = new SqliteCommand(insSql, connection, transaction);
                        insCmd.Parameters.AddWithValue("@Id", lvlId);
                        insCmd.Parameters.AddWithValue("@StationId", lDiff.StationId);
                        insCmd.Parameters.AddWithValue("@Level", lDiff.Level);
                        insCmd.Parameters.AddWithValue("@ConstructionTime", lDiff.NewConstructionTime ?? 0);
                        insCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await insCmd.ExecuteNonQueryAsync(ct);
                    }
                    else
                    {
                        var upSql = @"
                            UPDATE HideoutLevels SET
                                ConstructionTime = @ConstructionTime,
                                UpdatedAt = @UpdatedAt
                            WHERE Id = @Id OR (StationId = @StationId AND Level = @Level);";
                        await using var upCmd = new SqliteCommand(upSql, connection, transaction);
                        upCmd.Parameters.AddWithValue("@Id", lvlId);
                        upCmd.Parameters.AddWithValue("@StationId", lDiff.StationId);
                        upCmd.Parameters.AddWithValue("@Level", lDiff.Level);
                        upCmd.Parameters.AddWithValue("@ConstructionTime", lDiff.NewConstructionTime ?? 0);
                        upCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await upCmd.ExecuteNonQueryAsync(ct);
                    }
                }
                else if (lDiff.ChangeType == DiffChangeType.Modified)
                {
                    var upSql = @"
                        UPDATE HideoutLevels SET
                            ConstructionTime = CASE WHEN @ConstructionTime IS NOT NULL THEN @ConstructionTime ELSE ConstructionTime END,
                            UpdatedAt = @UpdatedAt
                        WHERE Id = @Id OR (StationId = @StationId AND Level = @Level);";
                    await using var upCmd = new SqliteCommand(upSql, connection, transaction);
                    upCmd.Parameters.AddWithValue("@Id", lvlId);
                    upCmd.Parameters.AddWithValue("@StationId", lDiff.StationId);
                    upCmd.Parameters.AddWithValue("@Level", lDiff.Level);
                    upCmd.Parameters.AddWithValue("@ConstructionTime", (object?)lDiff.NewConstructionTime ?? DBNull.Value);
                    upCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                    await upCmd.ExecuteNonQueryAsync(ct);
                }
                else if (lDiff.ChangeType == DiffChangeType.Removed)
                {
                    var delSql = "DELETE FROM HideoutLevels WHERE Id = @Id OR (StationId = @StationId AND Level = @Level);";
                    await using var delCmd = new SqliteCommand(delSql, connection, transaction);
                    delCmd.Parameters.AddWithValue("@Id", lvlId);
                    delCmd.Parameters.AddWithValue("@StationId", lDiff.StationId);
                    delCmd.Parameters.AddWithValue("@Level", lDiff.Level);
                    await delCmd.ExecuteNonQueryAsync(ct);
                }
            }
        }

        private async Task ApplyHideoutItemRequirementsAsync(SqliteConnection connection, SqliteTransaction transaction, DataDiffReport diff, CancellationToken ct)
        {
            if (!await TableExistsAsync(connection, transaction, "HideoutItemRequirements", ct))
                return;

            foreach (var hItem in diff.HideoutItemDiffs)
            {
                ct.ThrowIfCancellationRequested();

                var hId = string.IsNullOrWhiteSpace(hItem.RequirementId) ? Guid.NewGuid().ToString("N") : hItem.RequirementId;


                if (hItem.ChangeType == DiffChangeType.Added)
                {
                    var checkCmd = new SqliteCommand("SELECT COUNT(*) FROM HideoutItemRequirements WHERE Id = @Id OR (StationId = @StationId AND Level = @Level AND ItemId = @ItemId);", connection, transaction);
                    checkCmd.Parameters.AddWithValue("@Id", hId);
                    checkCmd.Parameters.AddWithValue("@StationId", hItem.StationId);
                    checkCmd.Parameters.AddWithValue("@Level", hItem.Level);
                    checkCmd.Parameters.AddWithValue("@ItemId", hItem.ItemId);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct)) > 0;

                    if (!exists)
                    {
                        var insSql = @"
                            INSERT INTO HideoutItemRequirements (
                                Id, StationId, Level, ItemId, ItemName, ItemNameKO, ItemNameJA, IconLink, Count, FoundInRaid, SortOrder, UpdatedAt
                            ) VALUES (
                                @Id, @StationId, @Level, @ItemId, @ItemName, @ItemNameKO, @ItemNameJA, @IconLink, @Count, @FoundInRaid, 0, @UpdatedAt
                            );";
                        await using var insCmd = new SqliteCommand(insSql, connection, transaction);
                        insCmd.Parameters.AddWithValue("@Id", hId);
                        insCmd.Parameters.AddWithValue("@StationId", hItem.StationId);
                        insCmd.Parameters.AddWithValue("@Level", hItem.Level);
                        insCmd.Parameters.AddWithValue("@ItemId", hItem.ItemId);
                        insCmd.Parameters.AddWithValue("@ItemName", hItem.ItemName);
                        insCmd.Parameters.AddWithValue("@ItemNameKO", (object?)hItem.RemoteItem?.ItemNameKO ?? DBNull.Value);
                        insCmd.Parameters.AddWithValue("@ItemNameJA", (object?)hItem.RemoteItem?.ItemNameJA ?? DBNull.Value);
                        insCmd.Parameters.AddWithValue("@IconLink", (object?)hItem.RemoteItem?.IconLink ?? DBNull.Value);
                        insCmd.Parameters.AddWithValue("@Count", hItem.NewQuantity ?? 1);
                        insCmd.Parameters.AddWithValue("@FoundInRaid", hItem.NewFoundInRaid == true ? 1 : 0);
                        insCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await insCmd.ExecuteNonQueryAsync(ct);
                    }
                    else
                    {
                        var upSql = @"
                            UPDATE HideoutItemRequirements SET
                                Count = @Count,
                                FoundInRaid = @FoundInRaid,
                                UpdatedAt = @UpdatedAt
                            WHERE Id = @Id OR (StationId = @StationId AND Level = @Level AND ItemId = @ItemId);";
                        await using var upCmd = new SqliteCommand(upSql, connection, transaction);
                        upCmd.Parameters.AddWithValue("@Id", hId);
                        upCmd.Parameters.AddWithValue("@StationId", hItem.StationId);
                        upCmd.Parameters.AddWithValue("@Level", hItem.Level);
                        upCmd.Parameters.AddWithValue("@ItemId", hItem.ItemId);
                        upCmd.Parameters.AddWithValue("@Count", hItem.NewQuantity ?? 1);
                        upCmd.Parameters.AddWithValue("@FoundInRaid", hItem.NewFoundInRaid == true ? 1 : 0);
                        upCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                        await upCmd.ExecuteNonQueryAsync(ct);
                    }
                }
                else if (hItem.ChangeType == DiffChangeType.Modified)
                {
                    var upSql = @"
                        UPDATE HideoutItemRequirements SET
                            Count = CASE WHEN @Count IS NOT NULL THEN @Count ELSE Count END,
                            FoundInRaid = CASE WHEN @FoundInRaid IS NOT NULL THEN @FoundInRaid ELSE FoundInRaid END,
                            UpdatedAt = @UpdatedAt
                        WHERE Id = @Id OR (StationId = @StationId AND Level = @Level AND (ItemId = @ItemId OR ItemName = @ItemName));";
                    await using var upCmd = new SqliteCommand(upSql, connection, transaction);
                    upCmd.Parameters.AddWithValue("@Id", hId);
                    upCmd.Parameters.AddWithValue("@StationId", hItem.StationId);
                    upCmd.Parameters.AddWithValue("@Level", hItem.Level);
                    upCmd.Parameters.AddWithValue("@ItemId", hItem.ItemId ?? string.Empty);
                    upCmd.Parameters.AddWithValue("@ItemName", hItem.ItemName ?? string.Empty);
                    upCmd.Parameters.AddWithValue("@Count", (object?)hItem.NewQuantity ?? DBNull.Value);
                    upCmd.Parameters.AddWithValue("@FoundInRaid", hItem.NewFoundInRaid.HasValue ? (hItem.NewFoundInRaid.Value ? 1 : 0) : DBNull.Value);
                    upCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
                    await upCmd.ExecuteNonQueryAsync(ct);
                }
                else if (hItem.ChangeType == DiffChangeType.Removed)
                {
                    var delSql = "DELETE FROM HideoutItemRequirements WHERE Id = @Id OR (StationId = @StationId AND Level = @Level AND (ItemId = @ItemId OR ItemName = @ItemName));";
                    await using var delCmd = new SqliteCommand(delSql, connection, transaction);
                    delCmd.Parameters.AddWithValue("@Id", hId);
                    delCmd.Parameters.AddWithValue("@StationId", hItem.StationId);
                    delCmd.Parameters.AddWithValue("@Level", hItem.Level);
                    delCmd.Parameters.AddWithValue("@ItemId", hItem.ItemId ?? string.Empty);
                    delCmd.Parameters.AddWithValue("@ItemName", hItem.ItemName ?? string.Empty);
                    await delCmd.ExecuteNonQueryAsync(ct);
                }
            }
        }

        #endregion

        #region Hot-Reload Orchestration

        /// <summary>
        /// Orchestrates in-memory cache refresh across all application services,
        /// re-initializes player progress and prerequisite graphs without restart,
        /// and refreshes active UI pages.
        /// </summary>
        public async Task OrchestrateHotReloadAsync()
        {
            _log.Info("Starting comprehensive hot-reload orchestration...");

            // 1. Clear pools to release locks
            SqliteConnection.ClearAllPools();

            // 2. Notify DatabaseUpdateService
            try
            {
                DatabaseUpdateService.Instance?.NotifyDatabaseUpdated();
            }
            catch (Exception ex)
            {
                _log.Warning($"Error notifying DatabaseUpdateService: {ex.Message}");
            }

            // 3. Refresh DB-backed caches
            try
            {
                if (QuestDbService.Instance != null)
                {
                    await QuestDbService.Instance.RefreshAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Error refreshing QuestDbService: {ex.Message}");
            }

            try
            {
                if (HideoutDbService.Instance != null)
                {
                    await HideoutDbService.Instance.RefreshAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Error refreshing HideoutDbService: {ex.Message}");
            }

            try
            {
                if (ItemDbService.Instance != null)
                {
                    await ItemDbService.Instance.RefreshAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Error refreshing ItemDbService: {ex.Message}");
            }

            try
            {
                if (QuestObjectiveDbService.Instance != null)
                {
                    await QuestObjectiveDbService.Instance.RefreshAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Error refreshing QuestObjectiveDbService: {ex.Message}");
            }

            try
            {
                if (TraderDbService.Instance != null)
                {
                    await TraderDbService.Instance.RefreshAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Error refreshing TraderDbService: {ex.Message}");
            }

            try
            {
                if (MapMarkerDbService.Instance != null)
                {
                    await MapMarkerDbService.Instance.RefreshAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Error refreshing MapMarkerDbService: {ex.Message}");
            }

            // 4. Critical Gap Fix: Re-initialize QuestProgressService, QuestGraphService, and HideoutProgressService
            try
            {
                if (QuestProgressService.Instance != null)
                {
                    await QuestProgressService.Instance.InitializeFromDbAsync();
                    var tasks = QuestProgressService.Instance.AllTasks.ToList();
                    QuestGraphService.Instance?.Initialize(tasks);
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Error reinitializing QuestProgressService / QuestGraphService: {ex.Message}");
            }

            try
            {
                if (HideoutProgressService.Instance != null && HideoutDbService.Instance != null)
                {
                    var stations = HideoutDbService.Instance.AllStations.ToList();
                    HideoutProgressService.Instance.Initialize(stations);
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Error reinitializing HideoutProgressService: {ex.Message}");
            }

            // 5. Trigger UI updates on the main thread if active
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                try
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        var mainWindow = System.Windows.Application.Current.MainWindow;
                        if (mainWindow != null)
                        {
                            var method = mainWindow.GetType().GetMethod(
                                "ReloadActivePagesAfterUpdateAsync",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            if (method != null)
                            {
                                if (method.Invoke(mainWindow, null) is Task task)
                                {
                                    await task;
                                }
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    _log.Warning($"Error dispatching UI refresh to MainWindow: {ex.Message}");
                }
            }

            _log.Info("Comprehensive hot-reload orchestration completed.");
        }

        #endregion

        #region Helper Methods

        private static async Task<bool> TableExistsAsync(SqliteConnection connection, SqliteTransaction transaction, string tableName, CancellationToken ct)
        {
            var sql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
            await using var cmd = new SqliteCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("@name", tableName);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            return count > 0;
        }

        #endregion
    }
}
