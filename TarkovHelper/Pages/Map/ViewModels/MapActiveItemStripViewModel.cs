using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TarkovHelper.Pages.Map.ViewModels;

/// <summary>
/// ViewModel representing a required item card in the "Required items for active tasks" strip.
/// </summary>
public class MapActiveItemStripViewModel : INotifyPropertyChanged
{
    private string _itemName = string.Empty;
    private string _itemNormalizedName = string.Empty;
    private bool _foundInRaid;
    private int _currentCount;
    private int _neededCount;
    private string _questName = string.Empty;
    private string _needKey = string.Empty;

    public string ItemName
    {
        get => _itemName;
        set { _itemName = value; OnPropertyChanged(); }
    }

    public string ItemNormalizedName
    {
        get => _itemNormalizedName;
        set { _itemNormalizedName = value; OnPropertyChanged(); }
    }

    public bool FoundInRaid
    {
        get => _foundInRaid;
        set
        {
            _foundInRaid = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FirBadgeText));
        }
    }

    public string FirBadgeText => FoundInRaid ? "FIR" : "NON-FIR";

    public int CurrentCount
    {
        get => _currentCount;
        set
        {
            _currentCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(QuantityText));
            OnPropertyChanged(nameof(IsComplete));
        }
    }

    public int NeededCount
    {
        get => _neededCount;
        set
        {
            _neededCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(QuantityText));
            OnPropertyChanged(nameof(IsComplete));
        }
    }

    public string QuantityText => $"x{NeededCount}";
    public string ProgressText => $"{CurrentCount}/{NeededCount}";
    public bool IsComplete => CurrentCount >= NeededCount;

    public string QuestName
    {
        get => _questName;
        set { _questName = value; OnPropertyChanged(); }
    }

    public string NeedKey
    {
        get => _needKey;
        set { _needKey = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}
