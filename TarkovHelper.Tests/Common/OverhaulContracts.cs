using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Services;
using TarkovHelper.ViewModels;

namespace TarkovHelper.Tests.Common
{
    #region UI & Test Fixture Models (F7-F15)

    public class TaskCardTestModel
    {
        public string QuestId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Trader { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string WikiUrl { get; set; } = string.Empty;
        public string MapBadge { get; set; } = string.Empty;
        public bool IsExpanded { get; set; } = true;
        public bool IsCompleted { get; set; }
        public int TotalRequirements { get; set; }
        public int MetRequirements { get; set; }

        public string RequirementsSummaryText => $"Requirements {MetRequirements} of {TotalRequirements} met";
        public string RequirementsPillText => $"{MetRequirements}/{TotalRequirements}";

        public List<SubObjectiveStepperTestItem> ItemSteppers { get; } = new();
        public List<SubObjectiveActionTestItem> ActionToggles { get; } = new();

        public int XpReward { get; set; }
        public Dictionary<string, double> TraderRepRewards { get; } = new();
        public List<string> ItemRewards { get; } = new();

        public int UnlocksNext { get; set; }
        public int Impact { get; set; }
        public string ImpactSummaryText => $"Unlocks next: {UnlocksNext} | Impact: {Impact}";

        public void ToggleExpand() => IsExpanded = !IsExpanded;

        public void MarkComplete()
        {
            IsCompleted = true;
            foreach (var s in ItemSteppers) s.SetMax();
            foreach (var a in ActionToggles) a.IsChecked = true;
            MetRequirements = TotalRequirements;
        }

        public void RecalculateMetRequirements()
        {
            int met = 0;
            met += ItemSteppers.Count(s => s.CurrentCount >= s.MaxCount);
            met += ActionToggles.Count(a => a.IsChecked);
            MetRequirements = met;
        }
    }

    public class SubObjectiveStepperTestItem
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int CurrentCount { get; set; }
        public int MaxCount { get; set; }
        public bool FoundInRaid { get; set; }

        public void Increment()
        {
            if (CurrentCount < MaxCount) CurrentCount++;
        }

        public void Decrement()
        {
            if (CurrentCount > 0) CurrentCount--;
        }

        public void SetMax()
        {
            CurrentCount = MaxCount;
        }
    }

    public class SubObjectiveActionTestItem
    {
        public string ObjectiveId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsChecked { get; set; }

        public void Toggle() => IsChecked = !IsChecked;
    }

    public class MapPillTestItem
    {
        public string MapKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int ActiveTaskCount { get; set; }
        public bool IsSelected { get; set; }
    }

    public class MapTrackerSyncHarness
    {
        private static MapTrackerSyncHarness? _instance;
        public static MapTrackerSyncHarness Instance => _instance ??= new MapTrackerSyncHarness();

        public TaskObjectiveWithLocation? PinnedObjective { get; private set; }
        public event Action<TaskObjectiveWithLocation?>? PinnedObjectiveChanged;

        public string? CurrentMapKey { get; private set; }
        public event Action<string>? MapChanged;

        public string? CurrentFloorId { get; private set; }
        public event Action<string>? FloorChanged;

        public void PinObjective(TaskObjectiveWithLocation? objective)
        {
            PinnedObjective = objective;
            PinnedObjectiveChanged?.Invoke(objective);
        }

        public void SetCurrentMap(string mapKey)
        {
            CurrentMapKey = mapKey;
            MapChanged?.Invoke(mapKey);
        }

        public void SetCurrentFloor(string floorId)
        {
            CurrentFloorId = floorId;
            FloorChanged?.Invoke(floorId);
        }

        public void Reset()
        {
            PinnedObjective = null;
            CurrentMapKey = null;
            CurrentFloorId = null;
        }
    }

    #endregion
}
