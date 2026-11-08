using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TarkovHelper.Pages.Map.ViewModels;

/// <summary>
/// ViewModel representing a required key chip in the "Required keys for active tasks" strip.
/// </summary>
public class MapActiveKeyStripViewModel : INotifyPropertyChanged
{
    private string _keyName = string.Empty;
    private string _shortName = string.Empty;
    private bool _inStash;
    private string _questName = string.Empty;
    private string _targetRoom = string.Empty;

    public string KeyName
    {
        get => _keyName;
        set
        {
            _keyName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(TooltipText));
        }
    }

    public string ShortName
    {
        get => _shortName;
        set
        {
            _shortName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName => !string.IsNullOrEmpty(ShortName) ? ShortName : KeyName;

    public bool InStash
    {
        get => _inStash;
        set
        {
            _inStash = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StashStatusText));
        }
    }

    public string StashStatusText => InStash ? "✓ Stash" : "✕ Missing";

    public string QuestName
    {
        get => _questName;
        set
        {
            _questName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TooltipText));
        }
    }

    public string TargetRoom
    {
        get => _targetRoom;
        set
        {
            _targetRoom = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TooltipText));
        }
    }

    public string TooltipText => string.IsNullOrEmpty(TargetRoom)
        ? $"{KeyName} ({QuestName})"
        : $"{KeyName} - {TargetRoom} ({QuestName})";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}
