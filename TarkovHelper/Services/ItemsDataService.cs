using System.Windows;
using TarkovHelper.Models;
using TarkovHelper.Pages; // For AggregatedItemViewModel and others

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for aggregating item requirements from quests and hideout modules.
    /// Extracted from ItemsPage.xaml.cs to improve maintainability.
    /// </summary>
    public class ItemsDataService
    {
        private static ItemsDataService? _instance;
        public static ItemsDataService Instance => _instance ??= new ItemsDataService();

        private QuestProgressService _questProgressService => QuestProgressService.Instance;
        private HideoutProgressService _hideoutProgressService => HideoutProgressService.Instance;
        private QuestGraphService _questGraphService => QuestGraphService.Instance;
        private LocalizationService _loc => LocalizationService.Instance;

        private static readonly HashSet<string> CurrencyItems = new(StringComparer.OrdinalIgnoreCase)
        {
            "roubles", "dollars", "euros"
        };

        private static bool IsCurrency(string normalizedName) => CurrencyItems.Contains(normalizedName);

        private static readonly Dictionary<string, string> CategoryMapping = new(StringComparer.OrdinalIgnoreCase)
        {
            // Provisions (Food & Drinks)
            { "Food", "Provisions" },
            { "Drinks", "Provisions" },
            // Medical
            { "Medkits", "Medical" },
            { "Medical supplies", "Medical" },
            { "Injury treatment", "Medical" },
            { "Stimulants", "Medical" },
            { "Drugs", "Medical" },
            // Gear
            { "Armor vests", "Gear" },
            { "Armor plates", "Gear" },
            { "Chest rigs", "Gear" },
            { "Backpacks", "Gear" },
            { "Headwear", "Gear" },
            { "Eyewear", "Gear" },
            { "Face cover", "Gear" },
            { "Earpieces", "Gear" },
            { "Armbands", "Gear" },
            { "Special equipment", "Gear" },
            // Barter items
            { "Electronics", "Barter" },
            { "Building materials", "Barter" },
            { "Flammable materials", "Barter" },
            { "Energy elements", "Barter" },
            { "Household goods", "Barter" },
            { "Tools", "Barter" },
            { "Valuables", "Barter" },
            { "Other", "Barter" },
            // Info & Keys
            { "Info items", "Info & Keys" },
            { "Keys", "Info & Keys" },
            { "Keycards", "Info & Keys" },
            { "Maps", "Info & Keys" },
            { "Extraction intel", "Info & Keys" },
            // Containers
            { "Containers & cases", "Containers" },
            { "Secure containers", "Containers" },
            // Money
            { "Money", "Money" },
            // Ammo
            { "Rounds", "Ammo" },
            { "Ammo boxes", "Ammo" },
            { "Shrapnel", "Ammo" },
            // Weapon mods
            { "Mounts", "Weapon Mods" },
            { "Stocks & chassis", "Weapon Mods" },
            { "Handguards", "Weapon Mods" },
            { "Barrels", "Weapon Mods" },
            { "Magazines", "Weapon Mods" },
            { "Flash hiders & muzzle brakes", "Weapon Mods" },
            { "Suppressors", "Weapon Mods" },
            { "Muzzle adapters", "Weapon Mods" },
            { "Iron sights", "Weapon Mods" },
            { "Pistol grips", "Weapon Mods" },
            { "Receivers and slides", "Weapon Mods" },
            { "Charging handles", "Weapon Mods" },
            { "Gas blocks", "Weapon Mods" },
            { "Foregrips", "Weapon Mods" },
            { "Auxiliary parts", "Weapon Mods" },
            { "Bipods", "Weapon Mods" },
            { "Underbarrel grenade launchers", "Weapon Mods" },
            // Optics
            { "Scopes", "Optics" },
            { "Assault scopes", "Optics" },
            { "Reflex sights", "Optics" },
            { "Compact reflex sights", "Optics" },
            { "Night vision scopes", "Optics" },
            { "Thermal vision sights", "Optics" },
            // Tactical devices
            { "Flashlights", "Tactical" },
            { "Tactical combo devices", "Tactical" },
            // Helmet mods
            { "Helmet mods", "Helmet Mods" },
            // Weapons
            { "Weapons", "Weapons" },
            // Quest items
            { "Quest Items", "Quest Items" },
            // Misc
            { "Posters", "Misc" },
            { "Dogtag", "Misc" },
        };

        public string GetParentCategory(string? category)
        {
            if (string.IsNullOrEmpty(category))
                return "Other";

            var baseCategory = category.Contains('|') ? category.Split('|')[0] : category;

            if (CategoryMapping.TryGetValue(baseCategory, out var parentCategory))
                return parentCategory;

            return baseCategory;
        }

        public async Task<List<AggregatedItemViewModel>> GetAggregatedItemsAsync(Dictionary<string, TarkovItem>? itemLookup)
        {
            // Get hideout requirements
            var hideoutItems = _hideoutProgressService.GetAllRemainingItemRequirements();

            // Get quest requirements
            var questItems = GetQuestItemRequirements(itemLookup);

            // Merge both sources
            var mergedItems = new Dictionary<string, AggregatedItemViewModel>(StringComparer.OrdinalIgnoreCase);

            // Add hideout items
            foreach (var kvp in hideoutItems)
            {
                var hideoutItem = kvp.Value;
                var (displayName, subtitle, showSubtitle) = GetLocalizedNames(
                    hideoutItem.ItemName, hideoutItem.ItemNameKo, hideoutItem.ItemNameJa);

                string? wikiLink = null;
                string? category = null;
                TarkovItem? itemInfo = null;
                if (itemLookup != null)
                {
                    if (!itemLookup.TryGetValue(hideoutItem.ItemNormalizedName, out itemInfo) && !string.IsNullOrEmpty(hideoutItem.ItemId))
                    {
                        itemLookup.TryGetValue(hideoutItem.ItemId, out itemInfo);
                    }
                }
                if (itemInfo != null)
                {
                    wikiLink = itemInfo.WikiLink;
                    category = itemInfo.Category;
                }

                mergedItems[kvp.Key] = new AggregatedItemViewModel
                {
                    ItemId = hideoutItem.ItemId,
                    ItemNormalizedName = hideoutItem.ItemNormalizedName,
                    DisplayName = displayName,
                    SubtitleName = subtitle,
                    SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed,
                    Category = category,
                    ParentCategory = GetParentCategory(category),
                    QuestCount = 0,
                    QuestFIRCount = 0,
                    HideoutCount = hideoutItem.HideoutCount,
                    HideoutFIRCount = hideoutItem.HideoutFIRCount,
                    TotalCount = hideoutItem.HideoutCount,
                    TotalFIRCount = hideoutItem.HideoutFIRCount,
                    FoundInRaid = hideoutItem.FoundInRaid,
                    IconLink = hideoutItem.IconLink,
                    WikiLink = wikiLink
                };
            }

            // Add/merge quest items
            foreach (var kvp in questItems)
            {
                var questItem = kvp.Value;
                if (mergedItems.TryGetValue(kvp.Key, out var existing))
                {
                    existing.QuestCount = questItem.QuestCount;
                    existing.QuestFIRCount = questItem.QuestFIRCount;
                    existing.TotalCount = existing.HideoutCount + questItem.QuestCount;
                    existing.TotalFIRCount = existing.HideoutFIRCount + questItem.QuestFIRCount;
                    if (questItem.FoundInRaid)
                        existing.FoundInRaid = true;
                    if (string.IsNullOrEmpty(existing.WikiLink))
                        existing.WikiLink = questItem.WikiLink;
                    if (string.IsNullOrEmpty(existing.Category))
                    {
                        existing.Category = questItem.Category;
                        existing.ParentCategory = GetParentCategory(questItem.Category);
                    }
                }
                else
                {
                    var (displayName, subtitle, showSubtitle) = GetLocalizedNames(
                        questItem.ItemName, questItem.ItemNameKo, questItem.ItemNameJa);

                    mergedItems[kvp.Key] = new AggregatedItemViewModel
                    {
                        ItemId = questItem.ItemId,
                        ItemNormalizedName = questItem.ItemNormalizedName,
                        DisplayName = displayName,
                        SubtitleName = subtitle,
                        SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed,
                        Category = questItem.Category,
                        ParentCategory = GetParentCategory(questItem.Category),
                        QuestCount = questItem.QuestCount,
                        QuestFIRCount = questItem.QuestFIRCount,
                        HideoutCount = 0,
                        HideoutFIRCount = 0,
                        TotalCount = questItem.QuestCount,
                        TotalFIRCount = questItem.QuestFIRCount,
                        FoundInRaid = questItem.FoundInRaid,
                        IconLink = questItem.IconLink,
                        WikiLink = questItem.WikiLink
                    };
                }
            }

            return mergedItems.Values.ToList();
        }

        private Dictionary<string, QuestItemAggregate> GetQuestItemRequirements(Dictionary<string, TarkovItem>? itemLookup)
        {
            var result = new Dictionary<string, QuestItemAggregate>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in _questProgressService.AllTasks)
            {
                var status = _questProgressService.GetStatus(task);
                if (status == QuestStatus.Done || status == QuestStatus.Failed || status == QuestStatus.Unavailable)
                    continue;

                if (task.RequiredItems == null)
                    continue;

                foreach (var questItem in task.RequiredItems)
                {
                    TarkovItem? itemInfo = null;
                    itemLookup?.TryGetValue(questItem.ItemNormalizedName, out itemInfo);

                    if (itemInfo == null)
                        continue;

                    if (string.Equals(itemInfo.Category, "Quest Items", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var itemName = itemInfo.Name;
                    var iconLink = itemInfo.IconLink;
                    var wikiLink = itemInfo.WikiLink;

                    var countToAdd = IsCurrency(questItem.ItemNormalizedName) ? 1 : questItem.Amount;
                    var firCountToAdd = questItem.FoundInRaid ? countToAdd : 0;

                    if (result.TryGetValue(questItem.ItemNormalizedName, out var existing))
                    {
                        existing.QuestCount += countToAdd;
                        if (questItem.FoundInRaid)
                        {
                            existing.QuestFIRCount += countToAdd;
                            existing.FoundInRaid = true;
                        }
                    }
                    else
                    {
                        result[questItem.ItemNormalizedName] = new QuestItemAggregate
                        {
                            ItemId = itemInfo?.Id ?? questItem.ItemNormalizedName,
                            ItemName = itemName,
                            ItemNameKo = itemInfo?.NameKo,
                            ItemNameJa = itemInfo?.NameJa,
                            ItemNormalizedName = questItem.ItemNormalizedName,
                            IconLink = iconLink,
                            WikiLink = wikiLink,
                            Category = itemInfo?.Category,
                            QuestCount = countToAdd,
                            QuestFIRCount = firCountToAdd,
                            FoundInRaid = questItem.FoundInRaid
                        };
                    }
                }
            }

            return result;
        }

        public List<QuestItemSourceViewModel> GetQuestSources(string itemNormalizedName)
        {
            if (string.IsNullOrWhiteSpace(itemNormalizedName))
                return new List<QuestItemSourceViewModel>();

            var progressService = NeededItemProgressService.Instance;
            var groupedSources = new Dictionary<string, QuestItemSourceViewModel>(StringComparer.OrdinalIgnoreCase);

            if (_questProgressService.AllTasks == null)
                return new List<QuestItemSourceViewModel>();

            foreach (var task in _questProgressService.AllTasks)
            {
                if (task == null)
                    continue;

                var status = _questProgressService.GetStatus(task);
                if (status == QuestStatus.Done || status == QuestStatus.Failed || status == QuestStatus.Unavailable)
                    continue;

                if (task.RequiredItems == null)
                    continue;

                foreach (var questItem in task.RequiredItems)
                {
                    if (questItem == null)
                        continue;

                    if (string.Equals(questItem.ItemNormalizedName, itemNormalizedName, StringComparison.OrdinalIgnoreCase))
                    {
                        var taskNormName = !string.IsNullOrWhiteSpace(task.NormalizedName)
                            ? task.NormalizedName
                            : (!string.IsNullOrWhiteSpace(task.Id) ? task.Id : (!string.IsNullOrWhiteSpace(task.Name) ? task.Name : "unknown_quest"));
                        var hasMinLevel = questItem.DogtagMinLevel.HasValue && questItem.DogtagMinLevel.Value > 0;
                        var groupKey = hasMinLevel
                            ? $"{taskNormName}:dogtag:{questItem.DogtagMinLevel!.Value}"
                            : taskNormName;

                        if (groupedSources.TryGetValue(groupKey, out var existing))
                        {
                            existing.Amount += Math.Max(0, questItem.Amount);
                            if (questItem.FoundInRaid)
                                existing.FoundInRaid = true;
                        }
                        else
                        {
                            var lang = LocalizationService.Instance.CurrentLanguage;
                            var questName = (lang == AppLanguage.KO && !string.IsNullOrEmpty(task.NameKo))
                                ? task.NameKo
                                : (lang == AppLanguage.JA && !string.IsNullOrEmpty(task.NameJa)
                                    ? task.NameJa
                                    : (task.Name ?? ""));
                            var needKey = NeededItemProgressService.GetQuestItemKey(taskNormName, itemNormalizedName, hasMinLevel ? questItem.DogtagMinLevel : null);

                            groupedSources[groupKey] = new QuestItemSourceViewModel
                            {
                                QuestName = questName,
                                TraderName = task.Trader ?? "",
                                ItemNormalizedName = itemNormalizedName,
                                NeedKey = needKey,
                                Amount = Math.Max(0, questItem.Amount),
                                FoundInRaid = questItem.FoundInRaid,
                                Task = task,
                                QuestNormalizedName = taskNormName,
                                DogtagMinLevel = hasMinLevel ? questItem.DogtagMinLevel : null
                            };
                        }
                    }
                }
            }

            foreach (var vm in groupedSources.Values)
            {
                vm.CurrentCount = Math.Clamp(progressService.GetCount(vm.NeedKey), 0, vm.Amount);
            }

            return groupedSources.Values.ToList();
        }

        public List<HideoutItemSourceViewModel> GetHideoutSources(string itemNormalizedName)
        {
            if (string.IsNullOrWhiteSpace(itemNormalizedName))
                return new List<HideoutItemSourceViewModel>();

            var progressService = NeededItemProgressService.Instance;
            var groupedSources = new Dictionary<string, HideoutItemSourceViewModel>(StringComparer.OrdinalIgnoreCase);

            if (_hideoutProgressService.AllModules == null)
                return new List<HideoutItemSourceViewModel>();

            foreach (var module in _hideoutProgressService.AllModules)
            {
                if (module?.Levels == null)
                    continue;

                var currentLevel = _hideoutProgressService.GetCurrentLevel(module);

                foreach (var level in module.Levels.Where(l => l != null && l.Level > currentLevel))
                {
                    if (level.ItemRequirements == null)
                        continue;

                    foreach (var itemReq in level.ItemRequirements)
                    {
                        if (itemReq == null)
                            continue;

                        if (string.Equals(itemReq.ItemNormalizedName, itemNormalizedName, StringComparison.OrdinalIgnoreCase))
                        {
                            var stationId = !string.IsNullOrWhiteSpace(module.Id)
                                ? module.Id
                                : (!string.IsNullOrWhiteSpace(module.NormalizedName) ? module.NormalizedName : (!string.IsNullOrWhiteSpace(module.Name) ? module.Name : "unknown_station"));
                            var groupKey = $"{stationId}:{level.Level}";

                            if (groupedSources.TryGetValue(groupKey, out var existing))
                            {
                                existing.Amount += Math.Max(0, itemReq.Count);
                                if (itemReq.FoundInRaid)
                                    existing.FoundInRaid = true;
                            }
                            else
                            {
                                var lang = LocalizationService.Instance.CurrentLanguage;
                                var moduleName = (lang == AppLanguage.KO && !string.IsNullOrEmpty(module.NameKo))
                                    ? module.NameKo
                                    : (lang == AppLanguage.JA && !string.IsNullOrEmpty(module.NameJa)
                                        ? module.NameJa
                                        : (module.Name ?? ""));
                                var needKey = NeededItemProgressService.GetHideoutItemKey(stationId, level.Level, itemNormalizedName);

                                groupedSources[groupKey] = new HideoutItemSourceViewModel
                                {
                                    ModuleName = moduleName,
                                    ItemNormalizedName = itemNormalizedName,
                                    NeedKey = needKey,
                                    Level = level.Level,
                                    Amount = Math.Max(0, itemReq.Count),
                                    FoundInRaid = itemReq.FoundInRaid,
                                    StationId = stationId
                                };
                            }
                        }
                    }
                }
            }

            foreach (var vm in groupedSources.Values)
            {
                vm.CurrentCount = Math.Clamp(progressService.GetCount(vm.NeedKey), 0, vm.Amount);
            }

            return groupedSources.Values.OrderBy(s => s.ModuleName).ThenBy(s => s.Level).ToList();
        }

    public async Task<List<CollectorItemViewModel>> GetCollectorAggregatedItemsAsync(bool includePreQuests, Dictionary<string, TarkovItem>? itemLookup)
    {
        var collectorItems = GetCollectorQuestItemRequirements(includePreQuests, itemLookup);

        return collectorItems.Values.Select(item =>
        {
            var (displayName, subtitle, showSubtitle) = GetLocalizedNames(
                item.ItemName, item.ItemNameKo, item.ItemNameJa);

            return new CollectorItemViewModel
            {
                ItemId = item.ItemId,
                ItemNormalizedName = item.ItemNormalizedName,
                DisplayName = displayName,
                SubtitleName = subtitle,
                SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed,
                QuestCount = item.QuestCount,
                QuestFIRCount = item.QuestFIRCount,
                TotalCount = item.QuestCount,
                TotalFIRCount = item.QuestFIRCount,
                FoundInRaid = item.FoundInRaid,
                IconLink = item.IconLink,
                WikiLink = item.WikiLink
            };
        }).ToList();
    }

    private Dictionary<string, CollectorQuestItemAggregate> GetCollectorQuestItemRequirements(bool includePreQuests, Dictionary<string, TarkovItem>? itemLookup)
    {
        var result = new Dictionary<string, CollectorQuestItemAggregate>(StringComparer.OrdinalIgnoreCase);
        var questsToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var collectorQuest = _questProgressService.AllTasks
            .FirstOrDefault(t => string.Equals(t.NormalizedName, "collector", StringComparison.OrdinalIgnoreCase));

        if (collectorQuest != null && !string.IsNullOrEmpty(collectorQuest.NormalizedName))
        {
            var status = _questProgressService.GetStatus(collectorQuest);
            if (status != QuestStatus.Done && status != QuestStatus.Failed && status != QuestStatus.Unavailable)
            {
                questsToInclude.Add(collectorQuest.NormalizedName);
            }

            if (includePreQuests)
            {
                var prereqs = _questGraphService.GetAllPrerequisites(collectorQuest.NormalizedName);
                foreach (var prereq in prereqs)
                {
                    if (string.IsNullOrEmpty(prereq.NormalizedName))
                        continue;

                    var prereqStatus = _questProgressService.GetStatus(prereq);
                    if (prereqStatus == QuestStatus.Done || prereqStatus == QuestStatus.Failed || prereqStatus == QuestStatus.Unavailable)
                        continue;

                    questsToInclude.Add(prereq.NormalizedName);
                }
            }
        }

        foreach (var task in _questProgressService.AllTasks)
        {
            if (string.IsNullOrEmpty(task.NormalizedName))
                continue;

            if (!questsToInclude.Contains(task.NormalizedName))
                continue;

            if (task.RequiredItems == null)
                continue;

            foreach (var questItem in task.RequiredItems)
            {
                TarkovItem? itemInfo = null;
                itemLookup?.TryGetValue(questItem.ItemNormalizedName, out itemInfo);

                if (itemInfo == null)
                    continue;

                var countToAdd = IsCurrency(questItem.ItemNormalizedName) ? 1 : questItem.Amount;
                var firCountToAdd = questItem.FoundInRaid ? countToAdd : 0;

                if (result.TryGetValue(questItem.ItemNormalizedName, out var existing))
                {
                    existing.QuestCount += countToAdd;
                    if (questItem.FoundInRaid)
                    {
                        existing.QuestFIRCount += countToAdd;
                        existing.FoundInRaid = true;
                    }
                }
                else
                {
                    result[questItem.ItemNormalizedName] = new CollectorQuestItemAggregate
                    {
                        ItemId = itemInfo?.Id ?? questItem.ItemNormalizedName,
                        ItemName = itemInfo.Name,
                        ItemNameKo = itemInfo.NameKo,
                        ItemNameJa = itemInfo.NameJa,
                        ItemNormalizedName = questItem.ItemNormalizedName,
                        IconLink = itemInfo.IconLink,
                        WikiLink = itemInfo.WikiLink,
                        QuestCount = countToAdd,
                        QuestFIRCount = firCountToAdd,
                        FoundInRaid = questItem.FoundInRaid
                    };
                }
            }
        }

        return result;
    }

    public List<CollectorQuestItemSourceViewModel> GetCollectorQuestSources(string itemNormalizedName, bool includePreQuests)
    {
        var sources = new List<CollectorQuestItemSourceViewModel>();
        var questsToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var collectorQuest = _questProgressService.AllTasks
            .FirstOrDefault(t => string.Equals(t.NormalizedName, "collector", StringComparison.OrdinalIgnoreCase));

        if (collectorQuest != null && !string.IsNullOrEmpty(collectorQuest.NormalizedName))
        {
            var status = _questProgressService.GetStatus(collectorQuest);
            if (status != QuestStatus.Done && status != QuestStatus.Failed && status != QuestStatus.Unavailable)
            {
                questsToInclude.Add(collectorQuest.NormalizedName);
            }

            if (includePreQuests)
            {
                var prereqs = _questGraphService.GetAllPrerequisites(collectorQuest.NormalizedName);
                foreach (var prereq in prereqs)
                {
                    if (string.IsNullOrEmpty(prereq.NormalizedName))
                        continue;
                    var prereqStatus = _questProgressService.GetStatus(prereq);
                    if (prereqStatus == QuestStatus.Done || prereqStatus == QuestStatus.Failed || prereqStatus == QuestStatus.Unavailable)
                        continue;
                    questsToInclude.Add(prereq.NormalizedName);
                }
            }
        }

        foreach (var task in _questProgressService.AllTasks)
        {
            if (string.IsNullOrEmpty(task.NormalizedName))
                continue;

            if (!questsToInclude.Contains(task.NormalizedName))
                continue;

            if (task.RequiredItems == null)
                continue;

            var matchingItems = task.RequiredItems
                .Where(qi => string.Equals(qi.ItemNormalizedName, itemNormalizedName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingItems.Count > 0)
            {
                sources.Add(new CollectorQuestItemSourceViewModel
                {
                    QuestName = (LocalizationService.Instance.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(task.NameKo))
                        ? task.NameKo
                        : task.Name,
                    TraderName = task.Trader,
                    Amount = matchingItems.Sum(qi => qi.Amount),
                    FoundInRaid = matchingItems.Any(qi => qi.FoundInRaid),
                    IsKappaRequired = task.ReqKappa,
                    Task = task,
                    QuestNormalizedName = task.NormalizedName ?? string.Empty
                });
            }
        }

        return sources;
    }

    private (string DisplayName, string Subtitle, bool ShowSubtitle) GetLocalizedNames(
        string name, string? nameKo, string? nameJa)
    {
        var lang = LocalizationService.Instance.CurrentLanguage;
        if (lang == AppLanguage.KO && !string.IsNullOrEmpty(nameKo))
        {
            return (nameKo, name, true);
        }
        if (lang == AppLanguage.JA && !string.IsNullOrEmpty(nameJa))
        {
            return (nameJa, name, true);
        }
        return (name, string.Empty, false);
    }
}
}
