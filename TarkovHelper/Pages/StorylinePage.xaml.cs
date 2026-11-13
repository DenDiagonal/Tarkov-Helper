using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TarkovHelper.Common;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper.Pages
{
    /// <summary>
    /// Interactive objective item inside a storyline chapter.
    /// </summary>
    public class StoryObjectiveItemViewModel : INotifyPropertyChanged
    {
        private bool _isChecked;
        public StoryChapterItemViewModel Chapter { get; }
        public int Index { get; }
        public string DisplayIndex => $"#{Index + 1:D2}";
        public string ObjectiveId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsOptional { get; set; }

        public Visibility OptionalVisibility => IsOptional ? Visibility.Visible : Visibility.Collapsed;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ToggleGlyph));
                    OnPropertyChanged(nameof(ToggleBackground));
                    OnPropertyChanged(nameof(ToggleForeground));
                    OnPropertyChanged(nameof(ToggleBorder));
                    OnPropertyChanged(nameof(TextDecorations));
                    OnPropertyChanged(nameof(TextOpacity));
                    Chapter?.OnObjectiveChanged();
                }
            }
        }

        public string ToggleGlyph => IsChecked ? "✓" : "";
        public Brush ToggleBackground => IsChecked ? TacticalBrushes.ActiveBg : TacticalBrushes.Surface3;
        public Brush ToggleForeground => IsChecked ? TacticalBrushes.ActiveFg : TacticalBrushes.TextSecondary;
        public Brush ToggleBorder => IsChecked ? TacticalBrushes.ActiveBorder : TacticalBrushes.BorderMedium;
        public TextDecorationCollection? TextDecorations => IsChecked ? System.Windows.TextDecorations.Strikethrough : null;
        public double TextOpacity => IsChecked ? 0.55 : 1.0;

        public StoryObjectiveItemViewModel(StoryChapterItemViewModel chapter, int index)
        {
            Chapter = chapter;
            Index = index;
        }

        public void Toggle()
        {
            IsChecked = !IsChecked;
            if (Chapter?.Task != null)
            {
                ObjectiveProgressService.Instance.SetObjectiveCompleted(
                    Chapter.Task.NormalizedName ?? string.Empty,
                    Index,
                    IsChecked,
                    ObjectiveId);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Storyline Chapter ViewModel (representing one of the 10 narrative story chapters).
    /// </summary>
    public class StoryChapterItemViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _searchFilter = string.Empty;

        public TarkovTask Task { get; }
        public string Name => Task.Name ?? Task.NormalizedName ?? "Unknown Chapter";
        public string WikiUrl => Task.WikiPageLink ?? (string.IsNullOrEmpty(Task.NormalizedName) ? string.Empty : $"https://escapefromtarkov.fandom.com/wiki/{Task.NormalizedName}");

        public ObservableCollection<StoryObjectiveItemViewModel> Objectives { get; } = new();
        public ICollectionView FilteredObjectives { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ItemBorderBrush));
                    OnPropertyChanged(nameof(ItemBgBrush));
                }
            }
        }

        public Brush ItemBorderBrush => IsSelected ? TacticalBrushes.BorderGold : TacticalBrushes.BorderSubtle;
        public Brush ItemBgBrush => IsSelected ? TacticalBrushes.Surface3 : TacticalBrushes.Surface2;

        public int TotalCount => Objectives.Count;
        public int MetCount => Objectives.Count(o => o.IsChecked);

        public double ProgressPercent
        {
            get => TotalCount > 0 ? Math.Round((double)MetCount / TotalCount * 100.0, 1) : 0;
            set { }
        }
        public string ProgressSummary => $"{MetCount} of {TotalCount} met ({ProgressPercent:0}%)";
        public string ShortProgress => $"{MetCount}/{TotalCount}";

        public bool IsCompleted => TotalCount > 0 && MetCount >= TotalCount;

        public string StatusText
        {
            get
            {
                if (IsCompleted) return "Completado";
                if (MetCount > 0) return "En progreso";
                return "Pendiente";
            }
        }

        public Brush StatusForeground
        {
            get
            {
                if (IsCompleted) return TacticalBrushes.ActiveFg;
                if (MetCount > 0) return TacticalBrushes.TacticalGold;
                return TacticalBrushes.TextMuted;
            }
        }

        public Brush StatusBackground
        {
            get
            {
                if (IsCompleted) return TacticalBrushes.ActiveBg;
                if (MetCount > 0) return TacticalBrushes.LevelLockedBg;
                return TacticalBrushes.Surface3;
            }
        }

        public Brush StatusBorder
        {
            get
            {
                if (IsCompleted) return TacticalBrushes.ActiveBorder;
                if (MetCount > 0) return TacticalBrushes.LevelLockedBorder;
                return TacticalBrushes.BorderSubtle;
            }
        }

        public Action? OnChangedCallback { get; set; }

        public StoryChapterItemViewModel(TarkovTask task)
        {
            Task = task;
            FilteredObjectives = CollectionViewSource.GetDefaultView(Objectives);
            FilteredObjectives.Filter = FilterObjective;
        }

        public void SetSearchFilter(string query)
        {
            _searchFilter = query?.Trim() ?? string.Empty;
            FilteredObjectives.Refresh();
        }

        private bool FilterObjective(object obj)
        {
            if (string.IsNullOrWhiteSpace(_searchFilter)) return true;
            if (obj is StoryObjectiveItemViewModel item)
            {
                return item.Description.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        public void OnObjectiveChanged()
        {
            OnPropertyChanged(nameof(MetCount));
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(ProgressSummary));
            OnPropertyChanged(nameof(ShortProgress));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusForeground));
            OnPropertyChanged(nameof(StatusBackground));
            OnPropertyChanged(nameof(StatusBorder));

            if (IsCompleted && !QuestProgressService.Instance.IsQuestCompleted(Task.NormalizedName ?? string.Empty))
            {
                QuestProgressService.Instance.CompleteQuest(Task, false);
            }
            else if (!IsCompleted && QuestProgressService.Instance.IsQuestCompleted(Task.NormalizedName ?? string.Empty))
            {
                QuestProgressService.Instance.ResetQuest(Task);
            }

            OnChangedCallback?.Invoke();
        }

        public void CompleteAll()
        {
            QuestProgressService.Instance.CompleteQuest(Task, false);
            foreach (var obj in Objectives)
            {
                if (!obj.IsChecked)
                {
                    obj.IsChecked = true;
                    ObjectiveProgressService.Instance.SetObjectiveCompleted(Task.NormalizedName ?? string.Empty, obj.Index, true, obj.ObjectiveId);
                }
            }
            OnObjectiveChanged();
        }

        public void ResetAll()
        {
            QuestProgressService.Instance.ResetQuest(Task);
            foreach (var obj in Objectives)
            {
                if (obj.IsChecked)
                {
                    obj.IsChecked = false;
                    ObjectiveProgressService.Instance.SetObjectiveCompleted(Task.NormalizedName ?? string.Empty, obj.Index, false, obj.ObjectiveId);
                }
            }
            OnObjectiveChanged();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Storyline page presenting all 10 narrative story chapters and their objectives.
    /// </summary>
    public partial class StorylinePage : UserControl, INotifyPropertyChanged
    {
        private static readonly List<string> CanonicalStoryOrder = new()
        {
            "Tour",
            "Falling Skies",
            "The Ticket",
            "Accidental Witness",
            "The Unheard",
            "They Are Already Here",
            "Batya",
            "Boreas",
            "Blue Fire",
            "The Labyrinth (story chapter)"
        };

        private StoryChapterItemViewModel? _selectedChapter;
        private string _searchQuery = string.Empty;

        public ObservableCollection<StoryChapterItemViewModel> Chapters { get; } = new();

        public StoryChapterItemViewModel? SelectedChapter
        {
            get => _selectedChapter;
            set
            {
                if (_selectedChapter != value)
                {
                    if (_selectedChapter != null) _selectedChapter.IsSelected = false;
                    _selectedChapter = value;
                    if (_selectedChapter != null)
                    {
                        _selectedChapter.IsSelected = true;
                        _selectedChapter.SetSearchFilter(_searchQuery);
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSelectedChapter));
                }
            }
        }

        public bool HasSelectedChapter => SelectedChapter != null;

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    OnPropertyChanged();
                    SelectedChapter?.SetSearchFilter(_searchQuery);
                }
            }
        }

        public int TotalChaptersCount => Chapters.Count;
        public int CompletedChaptersCount => Chapters.Count(c => c.IsCompleted);

        public int TotalObjectivesCount => Chapters.Sum(c => c.TotalCount);
        public int CompletedObjectivesCount => Chapters.Sum(c => c.MetCount);

        public double GlobalProgressPercent
        {
            get => TotalObjectivesCount > 0
                ? Math.Round((double)CompletedObjectivesCount / TotalObjectivesCount * 100.0, 1)
                : 0;
            set { }
        }

        public string GlobalChaptersSummary => $"{CompletedChaptersCount}/{TotalChaptersCount} Capítulos";
        public string GlobalObjectivesSummary => $"{CompletedObjectivesCount}/{TotalObjectivesCount} Objectives ({GlobalProgressPercent:0}%)";

        public StorylinePage()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            ObjectiveProgressService.Instance.ObjectiveProgressChanged += OnObjectiveProgressChanged;
            QuestProgressService.Instance.ProgressChanged += OnQuestProgressChanged;
            LoadChapters();
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            ObjectiveProgressService.Instance.ObjectiveProgressChanged -= OnObjectiveProgressChanged;
            QuestProgressService.Instance.ProgressChanged -= OnQuestProgressChanged;
        }

        private void OnObjectiveProgressChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(RefreshAllProgress);
        }

        private void OnQuestProgressChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(RefreshAllProgress);
        }

        public void LoadChapters()
        {
            Chapters.Clear();

            var allTasks = QuestProgressService.Instance.AllTasks;
            var storyTasks = allTasks
                .Where(t => string.Equals(t.Trader, "Story", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.Trader, "Story Chapters", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Order canonical chapters first
            var orderedTasks = storyTasks.OrderBy(t =>
            {
                var idx = CanonicalStoryOrder.FindIndex(name => string.Equals(name, t.Name, StringComparison.OrdinalIgnoreCase));
                return idx >= 0 ? idx : 999;
            }).ThenBy(t => t.Name).ToList();

            foreach (var task in orderedTasks)
            {
                var chapterVm = new StoryChapterItemViewModel(task)
                {
                    OnChangedCallback = RefreshAllProgress
                };

                bool isTaskDone = QuestProgressService.Instance.IsQuestCompleted(task.NormalizedName ?? string.Empty);

                if (task.Objectives != null)
                {
                    for (int i = 0; i < task.Objectives.Count; i++)
                    {
                        string rawText = task.Objectives[i];
                        bool isOpt = WikiMarkupHelper.IsOptional(rawText);
                        string cleaned = WikiMarkupHelper.RemoveOptionalMarker(rawText);
                        string objId = ObjectiveProgressService.Instance.ResolveObjectiveId(task.NormalizedName ?? string.Empty, i, null, task) ?? string.Empty;
                        bool isObjCompleted = isTaskDone || ObjectiveProgressService.Instance.IsObjectiveCompleted(task.NormalizedName ?? string.Empty, i);

                        var objVm = new StoryObjectiveItemViewModel(chapterVm, i)
                        {
                            ObjectiveId = objId,
                            Description = cleaned,
                            IsOptional = isOpt,
                            IsChecked = isObjCompleted
                        };
                        chapterVm.Objectives.Add(objVm);
                    }
                }

                Chapters.Add(chapterVm);
            }

            if (SelectedChapter == null || !Chapters.Contains(SelectedChapter))
            {
                SelectedChapter = Chapters.FirstOrDefault();
            }

            RefreshAllProgress();
        }

        public void RefreshAllProgress()
        {
            OnPropertyChanged(nameof(TotalChaptersCount));
            OnPropertyChanged(nameof(CompletedChaptersCount));
            OnPropertyChanged(nameof(TotalObjectivesCount));
            OnPropertyChanged(nameof(CompletedObjectivesCount));
            OnPropertyChanged(nameof(GlobalProgressPercent));
            OnPropertyChanged(nameof(GlobalChaptersSummary));
            OnPropertyChanged(nameof(GlobalObjectivesSummary));
        }

        private void LstChapters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstChapters.SelectedItem is StoryChapterItemViewModel selected)
            {
                SelectedChapter = selected;
            }
        }

        private void BtnToggleObjective_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StoryObjectiveItemViewModel obj)
            {
                obj.Toggle();
            }
        }

        private void BtnCompleteAll_Click(object sender, RoutedEventArgs e)
        {
            SelectedChapter?.CompleteAll();
        }

        private void BtnResetChapter_Click(object sender, RoutedEventArgs e)
        {
            SelectedChapter?.ResetAll();
        }

        private void BtnWiki_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(SelectedChapter?.WikiUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = SelectedChapter.WikiUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
