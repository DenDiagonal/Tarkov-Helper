using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TarkovHelper.Models;
using TarkovHelper.Pages;
using TarkovHelper.Services;

namespace TarkovHelper.ViewModels
{
    /// <summary>
    /// Dual view navigation modes for quest list.
    /// </summary>
    public enum NavigationMode
    {
        Traders,
        Maps
    }

    /// <summary>
    /// TarkovTracker 4-view navigation modes.
    /// </summary>
    public enum TasksViewMode
    {
        List,
        Traders,
        Maps,
        Graph
    }

    /// <summary>
    /// Unified quick filter statuses.
    /// </summary>
    public enum QuickFilterStatus
    {
        Active,
        Locked,
        Done,
        All,
        Failed
    }

    /// <summary>
    /// Canonical task status filters matching TarkovTracker chips.
    /// </summary>
    public enum TaskStatusFilter
    {
        All,
        Available,
        Locked,
        Completed,
        Failed
    }

    /// <summary>
    /// Task sorting modes matching TarkovTracker sort bar (8 canonical modes with backwards compatibility aliases).
    /// </summary>
    public enum TaskSortMode
    {
        Impact,
        DefaultOrder,
        Alphabetical,
        Name = Alphabetical,        // Backward compatibility alias
        PlayerLevel,
        Level = PlayerLevel,        // Backward compatibility alias
        Trader,                     // Alphabetical trader sorting (legacy compatibility)
        TraderOrder,                // Canonical trader order (TarkovTracker sequence)
        XpReward,
        XP = XpReward,              // Backward compatibility alias
        Progression,
        Teammates
    }

    /// <summary>
    /// Sort directions for quest lists.
    /// </summary>
    public enum SortDirection
    {
        Ascending,
        Descending
    }

    /// <summary>
    /// Summary record of live quest counts across all 5 statuses.
    /// </summary>
    public record StatusCountSummary(int All, int Available, int Locked, int Completed, int Failed);

    /// <summary>
    /// Encapsulates task reward details extracted from TarkovTracker data.
    /// </summary>
    public class TaskRewardItemDetail
    {
        public string ItemId { get; set; } = string.Empty;
        public string? BaseItemId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
        public int Count { get; set; } = 1;
    }

    public class TaskRewardInfo
    {
        public int Experience { get; set; }
        public Dictionary<string, double> TraderReputation { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ItemRewards { get; } = new();
        public List<TaskRewardItemDetail> DetailedItems { get; } = new();
    }

    /// <summary>
    /// Unified filter criteria for high-density QuestListPage and TarkovTracker parity.
    /// </summary>
    public class QuestFilterCriteria
    {
        public TasksViewMode ViewMode { get; set; } = TasksViewMode.List;

        public NavigationMode Mode
        {
            get => ViewMode == TasksViewMode.Maps ? NavigationMode.Maps : NavigationMode.Traders;
            set => ViewMode = value == NavigationMode.Maps ? TasksViewMode.Maps : TasksViewMode.Traders;
        }

        public string? SelectedTarget { get; set; } // "Prapor", "Customs", null/"Todos"/"All"

        /// <summary>
        /// Explicit trader filter facet (allows simultaneous Trader + Map filtering).
        /// </summary>
        public string? SelectedTrader { get; set; }

        /// <summary>
        /// Explicit map filter facet (allows simultaneous Trader + Map filtering).
        /// </summary>
        public string? SelectedMap { get; set; }

        public QuickFilterStatus StatusFilter { get; set; } = QuickFilterStatus.Active;

        public TaskStatusFilter TaskStatusFilter
        {
            get => StatusFilter switch
            {
                QuickFilterStatus.Active => TaskStatusFilter.Available,
                QuickFilterStatus.Locked => TaskStatusFilter.Locked,
                QuickFilterStatus.Done => TaskStatusFilter.Completed,
                QuickFilterStatus.All => TaskStatusFilter.All,
                QuickFilterStatus.Failed => TaskStatusFilter.Failed,
                _ => TaskStatusFilter.Available
            };
            set => StatusFilter = value switch
            {
                TaskStatusFilter.Available => QuickFilterStatus.Active,
                TaskStatusFilter.Locked => QuickFilterStatus.Locked,
                TaskStatusFilter.Completed => QuickFilterStatus.Done,
                TaskStatusFilter.All => QuickFilterStatus.All,
                TaskStatusFilter.Failed => QuickFilterStatus.Failed,
                _ => QuickFilterStatus.Active
            };
        }

        public TaskStatusFilter TaskStatus
        {
            get => TaskStatusFilter;
            set => TaskStatusFilter = value;
        }

        public TaskSortMode SortBy { get; set; } = TaskSortMode.Impact;
        public SortDirection SortDirection { get; set; } = SortDirection.Descending;

        public TaskSortMode SortMode
        {
            get => SortBy;
            set => SortBy = value;
        }

        // --- Task Types Section ---
        public bool TaskTypeRegular { get; set; } = true;
        public bool TaskTypeKappa { get; set; } = true;
        public bool TaskTypeLightkeeper { get; set; } = true;

        /// <summary>
        /// Backwards compatibility property for KappaOnly.
        /// When true, activates only Kappa tasks. When false, resets all task types to active.
        /// </summary>
        public bool KappaOnly
        {
            get => TaskTypeKappa && !TaskTypeRegular && !TaskTypeLightkeeper;
            set
            {
                if (value)
                {
                    TaskTypeKappa = true;
                    TaskTypeRegular = false;
                    TaskTypeLightkeeper = false;
                }
                else
                {
                    TaskTypeRegular = true;
                    TaskTypeKappa = true;
                    TaskTypeLightkeeper = true;
                }
            }
        }

        // --- Narrow Down Section ---
        public bool CurrentLevelOnly { get; set; }
        public string SearchQuery { get; set; } = string.Empty;
        public string? Faction { get; set; } // "bear", "usec", null = all
        public bool ItemRequiredOnly { get; set; }

        public bool NeedsKeys { get; set; }
        public bool KeyRequiredOnly
        {
            get => NeedsKeys;
            set => NeedsKeys = value;
        }

        public bool StoryOnly { get; set; }
        public bool ExcludeStoryChapters { get; set; }
        public bool TeamSharedOnly { get; set; }

        // --- Map Options Section ---
        public bool ShowAnyLocationTasks { get; set; } = true;
        public bool HideFinishedHere { get; set; } = false;

        // --- Behavior Settings ---
        public bool ImpactSortFollowsFilters { get; set; } = true;
    }

    /// <summary>
    /// High-performance filtering engine for TarkovHelper quest list.
    /// Supports dual Trader/Map modes, quick filter chips, and multi-attribute search.
    /// </summary>
    public class QuestFilterEngine
    {
        private static readonly Dictionary<string, int> CanonicalTraderOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Prapor"] = 1,
            ["Therapist"] = 2,
            ["Fence"] = 3,
            ["Skier"] = 4,
            ["Peacekeeper"] = 5,
            ["Mechanic"] = 6,
            ["Ragman"] = 7,
            ["Jaeger"] = 8,
            ["Ref"] = 9,
            ["Lightkeeper"] = 10,
            ["BTR Driver"] = 11,
            ["BTR"] = 11
        };

        /// <summary>
        /// Evaluates whether a QuestViewModel matches the specified criteria at current player level.
        /// </summary>
        public static bool Matches(QuestViewModel vm, QuestFilterCriteria criteria, int currentPlayerLevel = 15)
        {
            if (vm == null || vm.Task == null) return false;

            // Seasonal quests are only shown in Seasonal profile
            if (vm.Task.IsSeasonal && Services.ProfileService.Instance.CurrentProfile != Models.ProfileType.Seasonal)
            {
                return false;
            }

            // 1. Search Query filter (multi-attribute)
            if (!string.IsNullOrWhiteSpace(criteria.SearchQuery))
            {
                if (!MatchesSearchQuery(vm, criteria.SearchQuery))
                    return false;
            }

            // 2. Task Types filter (Regular, Kappa, Lightkeeper)
            bool isLightkeeper = string.Equals(vm.Task.Trader, "Lightkeeper", StringComparison.OrdinalIgnoreCase);
            bool isKappa = vm.IsKappaRequired || vm.Task.ReqKappa;
            bool isRegular = !isLightkeeper && !isKappa;

            bool typeAllowed = (criteria.TaskTypeRegular && isRegular) ||
                               (criteria.TaskTypeKappa && isKappa) ||
                               (criteria.TaskTypeLightkeeper && isLightkeeper);
            if (!typeAllowed)
                return false;

            // 3. Current Level Only filter (hide quests requiring higher level than player's level)
            if (criteria.CurrentLevelOnly)
            {
                if (vm.Task.RequiredLevel.HasValue && vm.Task.RequiredLevel.Value > currentPlayerLevel)
                    return false;
            }

            // 4. Item Required filter
            if (criteria.ItemRequiredOnly)
            {
                if (!vm.HasRequiredItems && !vm.HasFirRequirements && (vm.Task.RequiredItems == null || vm.Task.RequiredItems.Count == 0))
                    return false;
            }

            // 5. Key Required / Needs Keys filter
            if (criteria.KeyRequiredOnly || criteria.NeedsKeys)
            {
                if (!vm.HasNeededKeys)
                    return false;
            }

            // 6. Story filter (Story tasks belong to the dedicated Storyline tab)
            if (criteria.StoryOnly)
            {
                if (!vm.IsStoryChapter)
                    return false;
            }
            else if (criteria.ExcludeStoryChapters)
            {
                if (vm.IsStoryChapter)
                    return false;
            }

            // 7. Team Shared Only filter
            if (criteria.TeamSharedOnly)
            {
                if (!IsTeamSharedTask(vm))
                    return false;
            }

            // 8. Navigation Mode & Target (Trader vs Map with cross-filtering and map options)
            string? activeTrader = !string.IsNullOrWhiteSpace(criteria.SelectedTrader)
                ? criteria.SelectedTrader
                : ((criteria.ViewMode == TasksViewMode.Traders || criteria.Mode == NavigationMode.Traders) ? criteria.SelectedTarget : null);

            if (!string.IsNullOrWhiteSpace(activeTrader) &&
                !string.Equals(activeTrader, "Todos", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(activeTrader, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (!MatchesTrader(vm, activeTrader))
                    return false;
            }

            string? activeMap = !string.IsNullOrWhiteSpace(criteria.SelectedMap)
                ? criteria.SelectedMap
                : ((criteria.ViewMode == TasksViewMode.Maps || criteria.Mode == NavigationMode.Maps) ? criteria.SelectedTarget : null);

            if (!string.IsNullOrWhiteSpace(activeMap) &&
                !string.Equals(activeMap, "Todos", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(activeMap, "All", StringComparison.OrdinalIgnoreCase))
            {
                // Map option: Hide finished here
                if (criteria.HideFinishedHere && (vm.Status == QuestStatus.Done || vm.IsDone))
                {
                    return false;
                }

                bool matchesExplicitMap = MatchesMap(vm, activeMap);
                bool matchesAnyLocation = criteria.ShowAnyLocationTasks && IsAnyLocationTask(vm);

                if (!matchesExplicitMap && !matchesAnyLocation)
                    return false;
            }

            if ((criteria.ViewMode == TasksViewMode.List || criteria.ViewMode == TasksViewMode.Graph) &&
                !string.IsNullOrWhiteSpace(criteria.SelectedTarget) &&
                !string.Equals(criteria.SelectedTarget, "Todos", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(criteria.SelectedTarget, "All", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(activeTrader) && string.IsNullOrWhiteSpace(activeMap))
            {
                if (!MatchesTrader(vm, criteria.SelectedTarget) && !MatchesMap(vm, criteria.SelectedTarget))
                    return false;
            }

            // 9. Status Filter
            switch (criteria.StatusFilter)
            {
                case QuickFilterStatus.Active:
                    if (vm.Status != QuestStatus.Active)
                        return false;
                    break;
                case QuickFilterStatus.Locked:
                    if (vm.Status != QuestStatus.Locked && vm.Status != QuestStatus.LevelLocked)
                        return false;
                    break;
                case QuickFilterStatus.Done:
                    if (vm.Status != QuestStatus.Done)
                        return false;
                    break;
                case QuickFilterStatus.Failed:
                    if (vm.Status != QuestStatus.Failed)
                        return false;
                    break;
                case QuickFilterStatus.All:
                    // Allow all statuses
                    break;
            }

            // 10. Faction Filter
            if (!string.IsNullOrWhiteSpace(criteria.Faction) &&
                !string.Equals(criteria.Faction, "any", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(criteria.Faction, "all", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(vm.Task.Faction) &&
                !string.Equals(vm.Task.Faction, "any", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(vm.Task.Faction, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(vm.Task.Faction, criteria.Faction, StringComparison.OrdinalIgnoreCase))
                {
                    if (criteria.StatusFilter != QuickFilterStatus.All || vm.Status != QuestStatus.Unavailable)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Evaluates criteria ignoring the StatusFilter, useful for computing status chip counts.
        /// </summary>
        public static bool MatchesBaseFilters(QuestViewModel vm, QuestFilterCriteria criteria, int currentPlayerLevel = 15)
        {
            if (vm == null || vm.Task == null) return false;

            // 1. Search Query filter
            if (!string.IsNullOrWhiteSpace(criteria.SearchQuery))
            {
                if (!MatchesSearchQuery(vm, criteria.SearchQuery))
                    return false;
            }

            // 2. Task Types filter
            bool isLightkeeper = string.Equals(vm.Task.Trader, "Lightkeeper", StringComparison.OrdinalIgnoreCase);
            bool isKappa = vm.IsKappaRequired || vm.Task.ReqKappa;
            bool isRegular = !isLightkeeper && !isKappa;

            bool typeAllowed = (criteria.TaskTypeRegular && isRegular) ||
                               (criteria.TaskTypeKappa && isKappa) ||
                               (criteria.TaskTypeLightkeeper && isLightkeeper);
            if (!typeAllowed)
                return false;

            // 3. Current Level Only filter
            if (criteria.CurrentLevelOnly)
            {
                if (vm.Task.RequiredLevel.HasValue && vm.Task.RequiredLevel.Value > currentPlayerLevel)
                    return false;
            }

            // 4. Item Required filter
            if (criteria.ItemRequiredOnly)
            {
                if (!vm.HasRequiredItems && !vm.HasFirRequirements && (vm.Task.RequiredItems == null || vm.Task.RequiredItems.Count == 0))
                    return false;
            }

            // 5. Key Required / Needs Keys filter
            if (criteria.KeyRequiredOnly || criteria.NeedsKeys)
            {
                if (!vm.HasNeededKeys)
                    return false;
            }

            // 6. Story filter (Story tasks belong to the dedicated Storyline tab)
            if (criteria.StoryOnly)
            {
                if (!vm.IsStoryChapter)
                    return false;
            }
            else if (criteria.ExcludeStoryChapters)
            {
                if (vm.IsStoryChapter)
                    return false;
            }

            // 7. Team Shared Only filter
            if (criteria.TeamSharedOnly)
            {
                if (!IsTeamSharedTask(vm))
                    return false;
            }

            // 8. Navigation Mode & Target
            string? activeTrader = !string.IsNullOrWhiteSpace(criteria.SelectedTrader)
                ? criteria.SelectedTrader
                : ((criteria.ViewMode == TasksViewMode.Traders || criteria.Mode == NavigationMode.Traders) ? criteria.SelectedTarget : null);

            if (!string.IsNullOrWhiteSpace(activeTrader) &&
                !string.Equals(activeTrader, "Todos", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(activeTrader, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (!MatchesTrader(vm, activeTrader))
                    return false;
            }

            string? activeMap = !string.IsNullOrWhiteSpace(criteria.SelectedMap)
                ? criteria.SelectedMap
                : ((criteria.ViewMode == TasksViewMode.Maps || criteria.Mode == NavigationMode.Maps) ? criteria.SelectedTarget : null);

            if (!string.IsNullOrWhiteSpace(activeMap) &&
                !string.Equals(activeMap, "Todos", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(activeMap, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (criteria.HideFinishedHere && (vm.Status == QuestStatus.Done || vm.IsDone))
                {
                    return false;
                }

                bool matchesExplicitMap = MatchesMap(vm, activeMap);
                bool matchesAnyLocation = criteria.ShowAnyLocationTasks && IsAnyLocationTask(vm);

                if (!matchesExplicitMap && !matchesAnyLocation)
                    return false;
            }

            if ((criteria.ViewMode == TasksViewMode.List || criteria.ViewMode == TasksViewMode.Graph) &&
                !string.IsNullOrWhiteSpace(criteria.SelectedTarget) &&
                !string.Equals(criteria.SelectedTarget, "Todos", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(criteria.SelectedTarget, "All", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(activeTrader) && string.IsNullOrWhiteSpace(activeMap))
            {
                if (!MatchesTrader(vm, criteria.SelectedTarget) && !MatchesMap(vm, criteria.SelectedTarget))
                    return false;
            }

            // 9. Faction Filter
            if (!string.IsNullOrWhiteSpace(criteria.Faction) &&
                !string.Equals(criteria.Faction, "any", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(criteria.Faction, "all", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(vm.Task.Faction) &&
                !string.Equals(vm.Task.Faction, "any", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(vm.Task.Faction, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(vm.Task.Faction, criteria.Faction, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Matches search query across titles (all locales), map names, required items, and needed keys.
        /// </summary>
        public static bool MatchesSearchQuery(QuestViewModel vm, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string q = query.Trim();

            // 1. Quest Titles, Names and IDs
            if (!string.IsNullOrEmpty(vm.DisplayName) && vm.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(vm.SubtitleName) && vm.SubtitleName.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(vm.Task.Name) && vm.Task.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(vm.Task.NameKo) && vm.Task.NameKo.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(vm.Task.NameJa) && vm.Task.NameJa.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(vm.Task.Id) && vm.Task.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(vm.Task.NormalizedName) && vm.Task.NormalizedName.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;

            if (vm.Task.Ids != null && vm.Task.Ids.Any(id => id.Contains(q, StringComparison.OrdinalIgnoreCase)))
                return true;

            // 2. Maps (English, normalized and localized)
            if (vm.Task.Maps != null && vm.Task.Maps.Count > 0)
            {
                foreach (var map in vm.Task.Maps)
                {
                    if (map.Contains(q, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var localized = LocalizationService.Instance.GetLocalizedMapName(map);
                    if (!string.IsNullOrEmpty(localized) && localized.Contains(q, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var canonical = GetCanonicalMapDisplayName(map);
                    if (!string.IsNullOrEmpty(canonical) && canonical.Contains(q, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            // 3. Required Items
            if (vm.Task.RequiredItems != null && vm.Task.RequiredItems.Count > 0)
            {
                foreach (var ri in vm.Task.RequiredItems)
                {
                    if (!string.IsNullOrEmpty(ri.ItemDisplayName) && ri.ItemDisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (!string.IsNullOrEmpty(ri.ItemNormalizedName) && ri.ItemNormalizedName.Contains(q, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            // 4. Needed Keys
            if (vm.HasNeededKeys)
            {
                var keys = NeededKeysService.Instance.GetNeededKeysForQuest(vm.Task);
                if (keys != null && keys.Count > 0)
                {
                    foreach (var k in keys)
                    {
                        if (!string.IsNullOrEmpty(k.Name) && k.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                            return true;

                        if (!string.IsNullOrEmpty(k.ShortName) && k.ShortName.Contains(q, StringComparison.OrdinalIgnoreCase))
                            return true;

                        if (!string.IsNullOrEmpty(k.MapName) && k.MapName.Contains(q, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }

            // 5. Trader (English and localized)
            if (!string.IsNullOrEmpty(vm.Task.Trader) && vm.Task.Trader.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;

            var localizedTrader = LocalizationService.Instance.GetLocalizedTraderName(vm.Task.Trader ?? string.Empty);
            if (!string.IsNullOrEmpty(localizedTrader) && localizedTrader.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// Matches a quest's trader with a target trader key or display name.
        /// </summary>
        public static bool MatchesTrader(QuestViewModel vm, string targetTrader)
        {
            if (string.IsNullOrWhiteSpace(targetTrader)) return true;

            if (string.Equals(targetTrader, "Story", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetTrader, "Story Chapters", StringComparison.OrdinalIgnoreCase))
            {
                return vm.IsStoryChapter;
            }

            if (vm.IsStoryChapter) return false;

            string questTrader = vm.Task.Trader ?? string.Empty;
            if (string.Equals(questTrader, targetTrader, StringComparison.OrdinalIgnoreCase))
                return true;

            var friendly = QuestProgressService.ResolveTraderFriendlyName(questTrader);
            if (string.Equals(friendly, targetTrader, StringComparison.OrdinalIgnoreCase))
                return true;

            var targetFriendly = QuestProgressService.ResolveTraderFriendlyName(targetTrader);
            if (string.Equals(friendly, targetFriendly, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// Matches a quest's map list with a target map key or display name.
        /// </summary>
        public static bool MatchesMap(QuestViewModel vm, string targetMap)
        {
            if (string.IsNullOrWhiteSpace(targetMap)) return true;
            if (vm.Task.Maps == null || vm.Task.Maps.Count == 0) return false;

            string targetCanonical = NormalizeMapKey(targetMap);

            foreach (var map in vm.Task.Maps)
            {
                if (string.Equals(map, targetMap, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (string.Equals(NormalizeMapKey(map), targetCanonical, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a quest is a global task that can be completed on any map.
        /// </summary>
        public static bool IsAnyLocationTask(QuestViewModel vm)
        {
            if (vm?.Task == null) return false;
            if (vm.Task.Maps == null || vm.Task.Maps.Count == 0) return true;
            return vm.Task.Maps.Any(m => string.Equals(m, "any", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(m, "all", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(m, "global", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Checks if a quest is shared across all squad members / teammates.
        /// </summary>
        public static bool IsTeamSharedTask(QuestViewModel vm)
        {
            if (vm?.Task == null) return false;
            return (string.IsNullOrEmpty(vm.Task.Faction) || string.Equals(vm.Task.Faction, "any", StringComparison.OrdinalIgnoreCase)) &&
                   string.IsNullOrEmpty(vm.Task.RequiredEdition) &&
                   string.IsNullOrEmpty(vm.Task.ExcludedEdition);
        }

        /// <summary>
        /// Canonical map keys for normalizing various formats across all 12 EFT locations.
        /// Disambiguates 'the-labyrinth' from 'the-lab' by checking 'labyrinth' prior to 'lab'.
        /// </summary>
        public static string NormalizeMapKey(string map)
        {
            if (string.IsNullOrWhiteSpace(map)) return string.Empty;
            string clean = map.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");

            if (clean.Contains("groundzero") || clean.Contains("sandbox")) return "groundzero";
            if (clean.Contains("customs") || clean.Contains("bigmap")) return "customs";
            if (clean.Contains("woods")) return "woods";
            if (clean.Contains("factory")) return "factory";
            if (clean.Contains("interchange")) return "interchange";
            if (clean.Contains("shoreline")) return "shoreline";
            if (clean.Contains("reserve") || clean.Contains("rezerv")) return "reserve";
            if (clean.Contains("lighthouse")) return "lighthouse";
            if (clean.Contains("streets")) return "streets";
            if (clean.Contains("labyrinth")) return "thelabyrinth";
            if (clean.Contains("terminal")) return "terminal";
            if (clean.Contains("lab")) return "thelab";

            return clean;
        }

        /// <summary>
        /// Display name lookup for canonical quest maps across all 12 locations.
        /// </summary>
        public static string GetCanonicalMapDisplayName(string mapKey)
        {
            var normalized = NormalizeMapKey(mapKey);
            return normalized switch
            {
                "groundzero" => "Ground Zero",
                "customs" => "Customs",
                "woods" => "Woods",
                "factory" => "Factory",
                "interchange" => "Interchange",
                "shoreline" => "Shoreline",
                "reserve" => "Reserve",
                "lighthouse" => "Lighthouse",
                "streets" => "Streets of Tarkov",
                "thelab" => "The Lab",
                "thelabyrinth" => "The Labyrinth",
                "terminal" => "Terminal",
                _ => mapKey
            };
        }

        /// <summary>
        /// Calculates live count summary across all 5 task statuses (All, Available, Locked, Completed, Failed).
        /// </summary>
        public static StatusCountSummary ComputeStatusCounts(
            IEnumerable<QuestViewModel> quests,
            QuestFilterCriteria? baseCriteria = null,
            int currentPlayerLevel = 15)
        {
            if (quests == null) return new StatusCountSummary(0, 0, 0, 0, 0);

            int all = 0;
            int available = 0;
            int locked = 0;
            int completed = 0;
            int failed = 0;

            foreach (var q in quests)
            {
                if (q == null || q.Task == null) continue;

                if (baseCriteria != null && !MatchesBaseFilters(q, baseCriteria, currentPlayerLevel))
                {
                    continue;
                }

                all++;
                switch (q.Status)
                {
                    case QuestStatus.Active:
                        available++;
                        break;
                    case QuestStatus.Locked:
                    case QuestStatus.LevelLocked:
                        locked++;
                        break;
                    case QuestStatus.Done:
                        completed++;
                        break;
                    case QuestStatus.Failed:
                        failed++;
                        break;
                }
            }

            return new StatusCountSummary(all, available, locked, completed, failed);
        }

        private static readonly object _rewardsLock = new();
        private static bool _rewardsLoaded = false;
        private static readonly Dictionary<string, TaskRewardInfo> _taskRewardsLookup = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Ensures task rewards (XP, trader rep, items) are loaded from scratch/tasks_core.json.
        /// </summary>
        public static void EnsureRewardsLoaded()
        {
            if (_rewardsLoaded) return;
            lock (_rewardsLock)
            {
                if (_rewardsLoaded) return;
                try
                {
                    string[] rewardCandidatePaths = new[]
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Data", "tasks_rewards.json"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scratch", "tasks_rewards.json"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "scratch", "tasks_rewards.json"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "scratch", "tasks_rewards.json"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "scratch", "tasks_rewards.json"),
                        Path.Combine(Directory.GetCurrentDirectory(), "Assets", "DB", "Data", "tasks_rewards.json"),
                        Path.Combine(Directory.GetCurrentDirectory(), "scratch", "tasks_rewards.json"),
                        Path.Combine(Directory.GetCurrentDirectory(), "..", "scratch", "tasks_rewards.json"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Data", "tasks_core.json"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scratch", "tasks_core.json"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "scratch", "tasks_core.json"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "scratch", "tasks_core.json"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "scratch", "tasks_core.json"),
                        Path.Combine(Directory.GetCurrentDirectory(), "scratch", "tasks_core.json"),
                        Path.Combine(Directory.GetCurrentDirectory(), "..", "scratch", "tasks_core.json")
                    };

                    foreach (var path in rewardCandidatePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(path));
                        if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                            dataEl.TryGetProperty("tasks", out var tasksEl) &&
                            tasksEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var t in tasksEl.EnumerateArray())
                            {
                                var id = t.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                                var name = t.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                                var norm = t.TryGetProperty("normalizedName", out var normProp) ? normProp.GetString() : null;
                                int xp = t.TryGetProperty("experience", out var xpProp) && xpProp.TryGetInt32(out var exp) ? exp : 0;

                                TaskRewardInfo? existing = null;
                                if (!string.IsNullOrEmpty(id)) _taskRewardsLookup.TryGetValue(id, out existing);
                                if (existing == null && !string.IsNullOrEmpty(name)) _taskRewardsLookup.TryGetValue(name, out existing);
                                if (existing == null && !string.IsNullOrEmpty(norm)) _taskRewardsLookup.TryGetValue(norm, out existing);

                                var rewardInfo = existing ?? new TaskRewardInfo();
                                if (xp > 0 && rewardInfo.Experience == 0) rewardInfo.Experience = xp;

                                if (t.TryGetProperty("finishRewards", out var fr))
                                {
                                    if (fr.TryGetProperty("traderStanding", out var ts) && ts.ValueKind == JsonValueKind.Array && rewardInfo.TraderReputation.Count == 0)
                                    {
                                        foreach (var standingEl in ts.EnumerateArray())
                                        {
                                            string? traderName = null;
                                            if (standingEl.TryGetProperty("trader", out var traderObj))
                                            {
                                                if (traderObj.ValueKind == JsonValueKind.Object && traderObj.TryGetProperty("name", out var tNameProp))
                                                    traderName = tNameProp.GetString();
                                                else if (traderObj.ValueKind == JsonValueKind.String)
                                                    traderName = QuestProgressService.ResolveTraderFriendlyName(traderObj.GetString() ?? string.Empty);
                                            }
                                            if (!string.IsNullOrEmpty(traderName) &&
                                                standingEl.TryGetProperty("standing", out var stdProp) &&
                                                stdProp.TryGetDouble(out var stdVal) &&
                                                Math.Abs(stdVal) > 0.0001)
                                            {
                                                rewardInfo.TraderReputation[traderName] = stdVal;
                                            }
                                        }
                                    }

                                    if (fr.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array && rewardInfo.DetailedItems.Count == 0)
                                    {
                                        foreach (var it in itemsEl.EnumerateArray())
                                        {
                                            int count = it.TryGetProperty("count", out var cntProp) && cntProp.TryGetInt32(out var c) ? c : 1;
                                            string? itemId = null;
                                            string? baseItemId = null;
                                            string? itemName = null;
                                            string? shortName = null;

                                            if (it.TryGetProperty("item", out var iObj))
                                            {
                                                if (iObj.ValueKind == JsonValueKind.Object)
                                                {
                                                    if (iObj.TryGetProperty("id", out var iidProp)) itemId = iidProp.GetString();
                                                    if (iObj.TryGetProperty("baseItemId", out var bidProp) && bidProp.ValueKind == JsonValueKind.String) baseItemId = bidProp.GetString();
                                                    if (iObj.TryGetProperty("name", out var iNameProp)) itemName = iNameProp.GetString();
                                                    if (iObj.TryGetProperty("shortName", out var sNameProp)) shortName = sNameProp.GetString();
                                                }
                                                else if (iObj.ValueKind == JsonValueKind.String)
                                                {
                                                    itemId = iObj.GetString();
                                                }
                                            }

                                            if (string.IsNullOrEmpty(itemName) && !string.IsNullOrEmpty(itemId))
                                            {
                                                var dbItem = ItemDbService.Instance.GetItemById(itemId);
                                                if (dbItem != null)
                                                {
                                                    itemName = dbItem.Name;
                                                    shortName = !string.IsNullOrEmpty(dbItem.ShortName) ? dbItem.ShortName : dbItem.Name;
                                                }
                                                else
                                                {
                                                    itemName = itemId;
                                                    shortName = itemId;
                                                }
                                            }

                                            if (!string.IsNullOrEmpty(itemName))
                                            {
                                                string displayShort = !string.IsNullOrEmpty(shortName) ? shortName! : itemName!;
                                                rewardInfo.ItemRewards.Add(count > 1 ? $"{count}x {itemName}" : itemName);
                                                rewardInfo.DetailedItems.Add(new TaskRewardItemDetail
                                                {
                                                    ItemId = itemId ?? string.Empty,
                                                    BaseItemId = baseItemId,
                                                    Name = itemName!,
                                                    ShortName = displayShort,
                                                    Count = count
                                                });
                                            }
                                        }
                                    }
                                }

                                if (!string.IsNullOrEmpty(id)) _taskRewardsLookup[id] = rewardInfo;
                                if (!string.IsNullOrEmpty(name))
                                {
                                    _taskRewardsLookup[name] = rewardInfo;
                                    var strippedName = name.Replace(" [PVP ZONE]", "", StringComparison.OrdinalIgnoreCase).Trim();
                                    if (!string.IsNullOrEmpty(strippedName)) _taskRewardsLookup[strippedName] = rewardInfo;
                                }
                                if (!string.IsNullOrEmpty(norm)) _taskRewardsLookup[norm] = rewardInfo;
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback to empty rewards lookup gracefully
                }
                finally
                {
                    _rewardsLoaded = true;
                }
            }
        }

        /// <summary>
        /// Retrieves reward information (XP, trader rep, items) for a given quest.
        /// </summary>
        public static TaskRewardInfo? GetTaskRewards(TarkovTask? task)
        {
            if (task == null) return null;
            EnsureRewardsLoaded();

            if (!string.IsNullOrEmpty(task.Id) && _taskRewardsLookup.TryGetValue(task.Id, out var ri)) return ri;
            if (task.Ids != null)
            {
                foreach (var id in task.Ids)
                {
                    if (!string.IsNullOrEmpty(id) && _taskRewardsLookup.TryGetValue(id, out ri)) return ri;
                }
            }
            if (!string.IsNullOrEmpty(task.NormalizedName) && _taskRewardsLookup.TryGetValue(task.NormalizedName, out ri)) return ri;
            if (!string.IsNullOrEmpty(task.Name) && _taskRewardsLookup.TryGetValue(task.Name, out ri)) return ri;

            return null;
        }

        /// <summary>
        /// Retrieves XP reward for a given quest, with level-based fallback if unpopulated.
        /// </summary>
        public static int GetTaskXP(TarkovTask? task)
        {
            if (task == null) return 0;
            var info = GetTaskRewards(task);
            if (info != null && info.Experience > 0)
                return info.Experience;

            return (task.RequiredLevel ?? 1) * 1000;
        }

        /// <summary>
        /// Retrieves canonical rank index for traders.
        /// </summary>
        public static int GetTraderRank(string? trader)
        {
            if (string.IsNullOrWhiteSpace(trader)) return 99;
            var friendly = QuestProgressService.ResolveTraderFriendlyName(trader);
            if (CanonicalTraderOrder.TryGetValue(friendly, out int rank)) return rank;
            if (CanonicalTraderOrder.TryGetValue(trader.Trim(), out rank)) return rank;
            return 99;
        }

        /// <summary>
        /// Computes topological progression depth (prerequisite count) in the quest DAG.
        /// </summary>
        public static int GetProgressionDepth(TarkovTask? task)
        {
            if (task == null) return 0;
            try
            {
                if (!string.IsNullOrEmpty(task.NormalizedName))
                {
                    var prereqs = QuestGraphService.Instance.GetAllPrerequisites(task.NormalizedName);
                    if (prereqs != null) return prereqs.Count;
                }
            }
            catch
            {
                // Fallback to direct previous count if graph is uninitialized
            }
            return task.Previous?.Count ?? 0;
        }

        /// <summary>
        /// Evaluates squad / teammates unlock accessibility score for a quest.
        /// </summary>
        public static int GetTeammatesAvailabilityScore(QuestViewModel q)
        {
            if (q == null || q.Task == null) return 0;

            int score = 0;

            // Universal faction availability (both BEAR and USEC can take it)
            bool isUniversalFaction = string.IsNullOrEmpty(q.Task.Faction) ||
                                      string.Equals(q.Task.Faction, "any", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(q.Task.Faction, "all", StringComparison.OrdinalIgnoreCase);
            if (isUniversalFaction) score += 100;

            // Non-edition-exclusive
            if (string.IsNullOrEmpty(q.Task.RequiredEdition) && string.IsNullOrEmpty(q.Task.ExcludedEdition))
                score += 50;

            // Currently active
            if (q.Status == QuestStatus.Active) score += 50;
            else if (q.Status == QuestStatus.Done) score += 20;

            // Accessibility by required level
            int reqLevel = q.Task.RequiredLevel ?? 1;
            score += Math.Max(0, 50 - reqLevel);

            return score;
        }

        /// <summary>
        /// Sorts quests according to the specified 8 sort modes and direction.
        /// </summary>
        public static List<QuestViewModel> SortQuests(
            IEnumerable<QuestViewModel> quests,
            TaskSortMode sortMode,
            SortDirection direction = SortDirection.Descending,
            QuestFilterCriteria? criteria = null)
        {
            if (quests == null) return new List<QuestViewModel>();

            var list = quests.ToList();
            if (list.Count <= 1) return list;

            string? activeMap = !string.IsNullOrWhiteSpace(criteria?.SelectedMap)
                ? criteria.SelectedMap
                : ((criteria?.ViewMode == TasksViewMode.Maps || criteria?.Mode == NavigationMode.Maps) ? criteria.SelectedTarget : null);

            bool isSpecificMap = !string.IsNullOrWhiteSpace(activeMap) &&
                                 !string.Equals(activeMap, "all", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(activeMap, "todos", StringComparison.OrdinalIgnoreCase);

            if (isSpecificMap)
            {
                int GetMapTier(QuestViewModel q)
                {
                    bool isDirect = MatchesMap(q, activeMap!) && !IsAnyLocationTask(q);
                    return isDirect ? 0 : 1;
                }

                int GetStatusTier(QuestViewModel q)
                {
                    return q.Status switch
                    {
                        QuestStatus.Active => 0,
                        QuestStatus.Locked => 1,
                        QuestStatus.LevelLocked => 2,
                        QuestStatus.Done => 3,
                        _ => 4
                    };
                }

                if (sortMode == TaskSortMode.DefaultOrder)
                {
                    return direction == SortDirection.Ascending
                        ? list.OrderBy(GetMapTier)
                              .ThenBy(GetStatusTier)
                              .ThenBy(q => q.Task?.RequiredLevel ?? 1)
                              .ThenBy(q => GetTraderRank(q.Task?.Trader))
                              .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                        : list.OrderBy(GetMapTier)
                              .ThenBy(GetStatusTier)
                              .ThenByDescending(q => q.Task?.RequiredLevel ?? 1)
                              .ThenByDescending(q => GetTraderRank(q.Task?.Trader))
                              .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
                }
                else if (sortMode == TaskSortMode.Impact)
                {
                    return direction == SortDirection.Ascending
                        ? list.OrderBy(GetMapTier)
                              .ThenBy(GetStatusTier)
                              .ThenBy(q => GetTraderRank(q.Task?.Trader))
                              .ThenBy(q => q.Task?.RequiredLevel ?? 1)
                              .ThenBy(q => TaskImpactEngine.Instance.GetTaskImpact(q.Task?.NormalizedName ?? string.Empty).PriorityScore)
                              .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                        : list.OrderBy(GetMapTier)
                              .ThenBy(GetStatusTier)
                              .ThenBy(q => GetTraderRank(q.Task?.Trader))
                              .ThenBy(q => q.Task?.RequiredLevel ?? 1)
                              .ThenByDescending(q => TaskImpactEngine.Instance.GetTaskImpact(q.Task?.NormalizedName ?? string.Empty).PriorityScore)
                              .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
                }
                else if (sortMode == TaskSortMode.TraderOrder || sortMode == TaskSortMode.Trader)
                {
                    return direction == SortDirection.Ascending
                        ? list.OrderBy(GetMapTier)
                              .ThenBy(GetStatusTier)
                              .ThenBy(q => GetTraderRank(q.Task?.Trader))
                              .ThenBy(q => q.Task?.RequiredLevel ?? 1)
                              .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                        : list.OrderBy(GetMapTier)
                              .ThenBy(GetStatusTier)
                              .ThenByDescending(q => GetTraderRank(q.Task?.Trader))
                              .ThenBy(q => q.Task?.RequiredLevel ?? 1)
                              .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
                }
                else if (sortMode == TaskSortMode.PlayerLevel)
                {
                    return direction == SortDirection.Ascending
                        ? list.OrderBy(GetMapTier)
                              .ThenBy(GetStatusTier)
                              .ThenBy(q => q.Task?.RequiredLevel ?? 0)
                              .ThenBy(q => GetTraderRank(q.Task?.Trader))
                              .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                        : list.OrderBy(GetMapTier)
                              .ThenBy(GetStatusTier)
                              .ThenByDescending(q => q.Task?.RequiredLevel ?? 0)
                              .ThenBy(q => GetTraderRank(q.Task?.Trader))
                              .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
                }
                else if (sortMode == TaskSortMode.Alphabetical)
                {
                    return direction == SortDirection.Ascending
                        ? list.OrderBy(GetMapTier)
                              .ThenBy(GetStatusTier)
                              .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                        : list.OrderBy(GetMapTier)
                              .ThenBy(GetStatusTier)
                              .ThenByDescending(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
                }
            }

            return sortMode switch
            {
                TaskSortMode.Impact => direction == SortDirection.Ascending
                    ? list.OrderBy(q => TaskImpactEngine.Instance.GetTaskImpact(q.Task?.NormalizedName ?? string.Empty).PriorityScore)
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                    : list.OrderByDescending(q => TaskImpactEngine.Instance.GetTaskImpact(q.Task?.NormalizedName ?? string.Empty).PriorityScore)
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),

                TaskSortMode.DefaultOrder => direction == SortDirection.Ascending
                    ? list.ToList()
                    : ((IEnumerable<QuestViewModel>)list).Reverse().ToList(),

                TaskSortMode.Alphabetical => direction == SortDirection.Ascending
                    ? list.OrderBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                    : list.OrderByDescending(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),

                TaskSortMode.PlayerLevel => direction == SortDirection.Ascending
                    ? list.OrderBy(q => q.Task?.RequiredLevel ?? 0)
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                    : list.OrderByDescending(q => q.Task?.RequiredLevel ?? 0)
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),

                TaskSortMode.Trader => direction == SortDirection.Ascending
                    ? list.OrderBy(q => q.Task?.Trader ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                          .ThenBy(q => q.Task?.RequiredLevel ?? 0)
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                    : list.OrderByDescending(q => q.Task?.Trader ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                          .ThenByDescending(q => q.Task?.RequiredLevel ?? 0)
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),

                TaskSortMode.TraderOrder => direction == SortDirection.Ascending
                    ? list.OrderBy(q => GetTraderRank(q.Task?.Trader))
                          .ThenBy(q => q.Task?.RequiredLevel ?? 0)
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                    : list.OrderByDescending(q => GetTraderRank(q.Task?.Trader))
                          .ThenByDescending(q => q.Task?.RequiredLevel ?? 0)
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),

                TaskSortMode.XpReward => direction == SortDirection.Ascending
                    ? list.OrderBy(q => GetTaskXP(q.Task))
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                    : list.OrderByDescending(q => GetTaskXP(q.Task))
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),

                TaskSortMode.Progression => direction == SortDirection.Ascending
                    ? list.OrderBy(q => GetProgressionDepth(q.Task))
                          .ThenBy(q => q.Task?.RequiredLevel ?? 0)
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                    : list.OrderByDescending(q => GetProgressionDepth(q.Task))
                          .ThenByDescending(q => q.Task?.RequiredLevel ?? 0)
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),

                TaskSortMode.Teammates => direction == SortDirection.Ascending
                    ? list.OrderBy(q => GetTeammatesAvailabilityScore(q))
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList()
                    : list.OrderByDescending(q => GetTeammatesAvailabilityScore(q))
                          .ThenBy(q => q.DisplayName ?? q.Task?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),

                _ => list
            };
        }

        /// <summary>
        /// Filters a collection of quests according to the specified criteria and player level.
        /// </summary>
        public static List<QuestViewModel> Filter(
            IEnumerable<QuestViewModel> quests,
            QuestFilterCriteria criteria,
            int currentPlayerLevel = 15)
        {
            if (quests == null) return new List<QuestViewModel>();
            if (criteria == null) return quests.ToList();
            var filtered = quests.Where(q => Matches(q, criteria, currentPlayerLevel)).ToList();
            return SortQuests(filtered, criteria.SortBy, criteria.SortDirection, criteria);
        }

        /// <summary>
        /// Aggregates active quest counts per canonical trader plus "All".
        /// </summary>
        public static Dictionary<string, int> ComputeTraderActiveCounts(
            IEnumerable<QuestViewModel> quests,
            string? playerFaction = null)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var canonicalTraders = new[]
            {
                "Prapor", "Therapist", "Fence", "Skier", "Peacekeeper",
                "Mechanic", "Ragman", "Jaeger", "Ref", "Lightkeeper", "BTR Driver"
            };

            foreach (var trader in canonicalTraders)
            {
                counts[trader] = 0;
            }
            counts["All"] = 0;

            if (quests == null) return counts;

            var active = quests
                .Where(q => q != null && q.Task != null && q.Status == QuestStatus.Active &&
                            (string.IsNullOrEmpty(playerFaction) ||
                             string.IsNullOrEmpty(q.Task.Faction) ||
                             string.Equals(q.Task.Faction, "any", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(q.Task.Faction, playerFaction, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var trader in canonicalTraders)
            {
                counts[trader] = active.Count(q =>
                    string.Equals(q.Task?.Trader, trader, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(QuestProgressService.ResolveTraderFriendlyName(q.Task?.Trader ?? string.Empty), trader, StringComparison.OrdinalIgnoreCase));
            }

            counts["All"] = active.Count;
            return counts;
        }

        /// <summary>
        /// Aggregates active quest counts per canonical map key across all 12 EFT locations plus "All".
        /// </summary>
        public static Dictionary<string, int> ComputeMapActiveCounts(
            IEnumerable<QuestViewModel> quests,
            string? playerFaction = null)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var canonicalMaps = new[]
            {
                "ground-zero", "customs", "woods", "factory", "shoreline", "interchange",
                "reserve", "lighthouse", "streets", "the-lab", "the-labyrinth", "terminal"
            };

            foreach (var map in canonicalMaps)
            {
                counts[map] = 0;
            }
            counts["All"] = 0;

            if (quests == null) return counts;

            var active = quests
                .Where(q => q != null && q.Status == QuestStatus.Active &&
                            (string.IsNullOrEmpty(playerFaction) ||
                             string.IsNullOrEmpty(q.Task?.Faction) ||
                             string.Equals(q.Task.Faction, "any", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(q.Task.Faction, playerFaction, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var map in canonicalMaps)
            {
                counts[map] = active.Count(q => q.Task?.Maps != null && q.Task.Maps.Any(m =>
                    string.Equals(m, map, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.Replace("-", "").Replace("_", ""), map.Replace("-", "").Replace("_", ""), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(NormalizeMapKey(m), NormalizeMapKey(map), StringComparison.OrdinalIgnoreCase)));
            }

            counts["All"] = active.Count;
            return counts;
        }

        /// <summary>
        /// Toggles a quest's completion status between Active and Done in 1 click,
        /// invoking genuine domain services to update persistence and dependencies.
        /// </summary>
        public static void ToggleDone(QuestViewModel quest)
        {
            ArgumentNullException.ThrowIfNull(quest);
            ArgumentNullException.ThrowIfNull(quest.Task);

            if (quest.IsDone)
            {
                QuestProgressService.Instance.ResetQuest(quest.Task);
                quest.Status = QuestStatus.Active;
            }
            else
            {
                bool completePrereqs = !quest.IsStoryChapter;
                QuestProgressService.Instance.CompleteQuest(quest.Task, completePrereqs);
                quest.Status = QuestStatus.Done;
            }
        }

        /// <summary>
        /// Retrieves the target map key for navigation/mapping launchers.
        /// </summary>
        public static string? GetTargetMap(QuestViewModel quest)
        {
            return quest?.Task?.Maps?.FirstOrDefault();
        }

        /// <summary>
        /// Resolves the canonical wiki URL for a quest.
        /// </summary>
        public static string? GetWikiUrl(QuestViewModel quest)
        {
            return quest?.Task != null ? NormalizedNameGenerator.GetQuestWikiUrl(quest.Task) : null;
        }
    }
}
