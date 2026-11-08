using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TarkovHelper.Pages.Map.ViewModels;

/// <summary>
/// ViewModel representing a canonical map pill with live active quest counts.
/// </summary>
public class MapPillItem : INotifyPropertyChanged
{
    private string _mapKey = string.Empty;
    private string _displayName = string.Empty;
    private int _activeTaskCount;
    private bool _isSelected;

    public string MapKey
    {
        get => _mapKey;
        set { _mapKey = value; OnPropertyChanged(); }
    }

    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    public int ActiveTaskCount
    {
        get => _activeTaskCount;
        set { _activeTaskCount = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}
