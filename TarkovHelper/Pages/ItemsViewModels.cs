using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TarkovHelper.Models;

namespace TarkovHelper.Pages
{
    /// <summary>
    /// Aggregated item view model for display with inventory tracking
    /// </summary>
    public class AggregatedItemViewModel : INotifyPropertyChanged
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemNormalizedName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string SubtitleName { get; set; } = string.Empty;
        public Visibility SubtitleVisibility { get; set; } = Visibility.Collapsed;
        public string? Category { get; set; }
        public string ParentCategory { get; set; } = "Other";
        public int QuestCount { get; set; }
        public int QuestFIRCount { get; set; }
        public int HideoutCount { get; set; }
        public int HideoutFIRCount { get; set; }
        public int TotalCount { get; set; }
        public int TotalFIRCount { get; set; }
        public bool FoundInRaid { get; set; }
        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;

        private BitmapImage? _iconSource;
        public BitmapImage? IconSource
        {
            get => _iconSource;
            set
            {
                if (_iconSource != value)
                {
                    _iconSource = value;
                    OnPropertyChanged(nameof(IconSource));
                }
            }
        }
        public string? IconLink { get; set; }
        public string? WikiLink { get; set; }

        // Inventory quantities (user's owned items)
        private int _ownedFirQuantity;
        private int _ownedNonFirQuantity;

        public int OwnedFirQuantity
        {
            get => _ownedFirQuantity;
            set
            {
                if (_ownedFirQuantity != value)
                {
                    _ownedFirQuantity = value;
                    OnPropertyChanged(nameof(OwnedFirQuantity));
                    OnPropertyChanged(nameof(OwnedTotalQuantity));
                    OnPropertyChanged(nameof(FulfillmentStatus));
                    OnPropertyChanged(nameof(ProgressPercent));
                    OnPropertyChanged(nameof(IsFulfilled));
                    OnPropertyChanged(nameof(FulfilledVisibility));
                    OnPropertyChanged(nameof(ItemOpacity));
                    OnPropertyChanged(nameof(NameTextDecorations));
                    OnPropertyChanged(nameof(OwnedDisplay));
                }
            }
        }

        public int OwnedNonFirQuantity
        {
            get => _ownedNonFirQuantity;
            set
            {
                if (_ownedNonFirQuantity != value)
                {
                    _ownedNonFirQuantity = value;
                    OnPropertyChanged(nameof(OwnedNonFirQuantity));
                    OnPropertyChanged(nameof(OwnedTotalQuantity));
                    OnPropertyChanged(nameof(FulfillmentStatus));
                    OnPropertyChanged(nameof(ProgressPercent));
                    OnPropertyChanged(nameof(IsFulfilled));
                    OnPropertyChanged(nameof(FulfilledVisibility));
                    OnPropertyChanged(nameof(ItemOpacity));
                    OnPropertyChanged(nameof(NameTextDecorations));
                    OnPropertyChanged(nameof(OwnedDisplay));
                }
            }
        }

        public int OwnedTotalQuantity => OwnedFirQuantity + OwnedNonFirQuantity;

        // Fulfillment calculation
        public ItemFulfillmentStatus FulfillmentStatus
        {
            get
            {
                if (TotalFIRCount > 0)
                {
                    // FIR is required
                    if (OwnedFirQuantity >= TotalFIRCount)
                        return ItemFulfillmentStatus.Fulfilled;
                    if (OwnedTotalQuantity > 0)
                        return ItemFulfillmentStatus.PartiallyFulfilled;
                    return ItemFulfillmentStatus.NotStarted;
                }
                else
                {
                    // Non-FIR OK
                    if (OwnedTotalQuantity >= TotalCount)
                        return ItemFulfillmentStatus.Fulfilled;
                    if (OwnedTotalQuantity > 0)
                        return ItemFulfillmentStatus.PartiallyFulfilled;
                    return ItemFulfillmentStatus.NotStarted;
                }
            }
        }

        public double ProgressPercent
        {
            get
            {
                if (TotalCount == 0) return 100;

                if (TotalFIRCount > 0)
                {
                    return Math.Min(100, (double)OwnedFirQuantity / TotalFIRCount * 100);
                }
                else
                {
                    return Math.Min(100, (double)OwnedTotalQuantity / TotalCount * 100);
                }
            }
        }

        public bool IsFulfilled => FulfillmentStatus == ItemFulfillmentStatus.Fulfilled;
        public Visibility FulfilledVisibility => IsFulfilled ? Visibility.Visible : Visibility.Collapsed;
        public double ItemOpacity => IsFulfilled ? 0.5 : 1.0;
        public TextDecorationCollection? NameTextDecorations => IsFulfilled ? TextDecorations.Strikethrough : null;

        // Owned display string
        public string OwnedDisplay
        {
            get
            {
                if (OwnedTotalQuantity == 0)
                    return "0";
                if (OwnedNonFirQuantity == 0)
                    return $"{OwnedFirQuantity}F";
                if (OwnedFirQuantity == 0)
                    return OwnedNonFirQuantity.ToString();
                return $"{OwnedFirQuantity}F+{OwnedNonFirQuantity}";
            }
        }

        // Display strings for UI - shows FIR/non-FIR breakdown
        public string QuestDisplay => QuestCount > 0 ? FormatCountDisplay(QuestCount, QuestFIRCount) : "0";
        public string HideoutDisplay => HideoutCount > 0 ? FormatCountDisplay(HideoutCount, HideoutFIRCount) : "0";
        public string TotalDisplay => FormatCountDisplay(TotalCount, TotalFIRCount);

        private static string FormatCountDisplay(int total, int firCount)
        {
            if (firCount == 0)
                return total.ToString();
            if (firCount == total)
                return $"{total} (FIR)";
            // Mixed: show both FIR and non-FIR counts
            var nonFirCount = total - firCount;
            return $"{firCount}F+{nonFirCount}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Quest item source - shows which quest requires this item with interactive progress stepper
    /// </summary>
    public class QuestItemSourceViewModel : INotifyPropertyChanged
    {
        private static readonly Brush CompleteBrush;
        private static readonly Brush IncompleteBrush;

        static QuestItemSourceViewModel()
        {
            var cBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            cBrush.Freeze();
            CompleteBrush = cBrush;

            var iBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            iBrush.Freeze();
            IncompleteBrush = iBrush;
        }

        public string QuestName { get; set; } = string.Empty;
        public string TraderName { get; set; } = string.Empty;
        public string ItemNormalizedName { get; set; } = string.Empty;
        public string NeedKey { get; set; } = string.Empty;
        public bool FoundInRaid { get; set; }
        public string? WikiLink { get; set; }
        public TarkovTask? Task { get; set; }

        private int _amount;
        public int Amount
        {
            get => _amount;
            set
            {
                if (_amount != value)
                {
                    _amount = value;
                    OnPropertyChanged(nameof(Amount));
                    OnPropertyChanged(nameof(AmountDisplay));
                    OnPropertyChanged(nameof(ProgressText));
                    OnPropertyChanged(nameof(IsComplete));
                    OnPropertyChanged(nameof(ProgressBrush));
                }
            }
        }

        private int _currentCount;
        public int CurrentCount
        {
            get => _currentCount;
            set
            {
                if (_currentCount != value)
                {
                    _currentCount = value;
                    OnPropertyChanged(nameof(CurrentCount));
                    OnPropertyChanged(nameof(ProgressText));
                    OnPropertyChanged(nameof(IsComplete));
                    OnPropertyChanged(nameof(ProgressBrush));
                }
            }
        }

        public string AmountDisplay => $"x{Amount}";
        public string ProgressText => $"{CurrentCount}/{Amount}";
        public bool IsComplete => CurrentCount >= Amount && Amount > 0;
        public Brush ProgressBrush => IsComplete ? CompleteBrush : IncompleteBrush;

        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;
        public Visibility WikiButtonVisibility => Task != null ? Visibility.Visible : Visibility.Collapsed;

        // Navigation identifier
        public string QuestNormalizedName { get; set; } = string.Empty;

        // Dogtag level requirement
        public int? DogtagMinLevel { get; set; }
        public bool HasDogtagLevel => DogtagMinLevel.HasValue;
        public Visibility DogtagLevelVisibility => HasDogtagLevel ? Visibility.Visible : Visibility.Collapsed;
        public string DogtagLevelDisplay => DogtagMinLevel.HasValue ? $"(Lv.{DogtagMinLevel}+)" : "";

        public void Increment()
        {
            if (string.IsNullOrEmpty(NeedKey)) return;
            CurrentCount = TarkovHelper.Services.NeededItemProgressService.Instance.Increment(NeedKey, Amount);
        }

        public void Decrement()
        {
            if (string.IsNullOrEmpty(NeedKey)) return;
            CurrentCount = TarkovHelper.Services.NeededItemProgressService.Instance.Decrement(NeedKey, Amount);
        }

        public void ToggleComplete()
        {
            if (string.IsNullOrEmpty(NeedKey)) return;
            CurrentCount = TarkovHelper.Services.NeededItemProgressService.Instance.ToggleComplete(NeedKey, Amount);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Hideout item source - shows which hideout module requires this item with interactive progress stepper
    /// </summary>
    public class HideoutItemSourceViewModel : INotifyPropertyChanged
    {
        private static readonly Brush CompleteBrush;
        private static readonly Brush IncompleteBrush;

        static HideoutItemSourceViewModel()
        {
            var cBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            cBrush.Freeze();
            CompleteBrush = cBrush;

            var iBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            iBrush.Freeze();
            IncompleteBrush = iBrush;
        }

        public string ModuleName { get; set; } = string.Empty;
        public string ItemNormalizedName { get; set; } = string.Empty;
        public string NeedKey { get; set; } = string.Empty;
        public int Level { get; set; }
        public bool FoundInRaid { get; set; }

        private int _amount;
        public int Amount
        {
            get => _amount;
            set
            {
                if (_amount != value)
                {
                    _amount = value;
                    OnPropertyChanged(nameof(Amount));
                    OnPropertyChanged(nameof(AmountDisplay));
                    OnPropertyChanged(nameof(ProgressText));
                    OnPropertyChanged(nameof(IsComplete));
                    OnPropertyChanged(nameof(ProgressBrush));
                }
            }
        }

        private int _currentCount;
        public int CurrentCount
        {
            get => _currentCount;
            set
            {
                if (_currentCount != value)
                {
                    _currentCount = value;
                    OnPropertyChanged(nameof(CurrentCount));
                    OnPropertyChanged(nameof(ProgressText));
                    OnPropertyChanged(nameof(IsComplete));
                    OnPropertyChanged(nameof(ProgressBrush));
                }
            }
        }

        public string LevelDisplay => $"Level {Level}";
        public string AmountDisplay => $"x{Amount}";
        public string ProgressText => $"{CurrentCount}/{Amount}";
        public bool IsComplete => CurrentCount >= Amount && Amount > 0;
        public Brush ProgressBrush => IsComplete ? CompleteBrush : IncompleteBrush;

        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;

        // Navigation identifier
        public string StationId { get; set; } = string.Empty;

        public void Increment()
        {
            if (string.IsNullOrEmpty(NeedKey)) return;
            CurrentCount = TarkovHelper.Services.NeededItemProgressService.Instance.Increment(NeedKey, Amount);
        }

        public void Decrement()
        {
            if (string.IsNullOrEmpty(NeedKey)) return;
            CurrentCount = TarkovHelper.Services.NeededItemProgressService.Instance.Decrement(NeedKey, Amount);
        }

        public void ToggleComplete()
        {
            if (string.IsNullOrEmpty(NeedKey)) return;
            CurrentCount = TarkovHelper.Services.NeededItemProgressService.Instance.ToggleComplete(NeedKey, Amount);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Quest item aggregate for internal processing
    /// </summary>
    internal class QuestItemAggregate
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? ItemNameKo { get; set; }
        public string? ItemNameJa { get; set; }
        public string ItemNormalizedName { get; set; } = string.Empty;
        public string? IconLink { get; set; }
        public string? WikiLink { get; set; }
        public string? Category { get; set; }
        public int QuestCount { get; set; }
        public int QuestFIRCount { get; set; }
        public bool FoundInRaid { get; set; }
    }
}
