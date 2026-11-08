using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TarkovHelper.Services;
using TarkovHelper.ViewModels;

namespace TarkovHelper.Pages
{
    public partial class DashboardPage : UserControl
    {
        public DashboardViewModel ViewModel { get; }

        public DashboardPage()
        {
            InitializeComponent();
            ViewModel = new DashboardViewModel();
            DataContext = ViewModel;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            QuestProgressService.Instance.ProgressChanged -= OnProgressChanged;
            QuestProgressService.Instance.ProgressChanged += OnProgressChanged;

            SettingsService.Instance.PlayerLevelChanged -= OnPlayerLevelChanged;
            SettingsService.Instance.PlayerLevelChanged += OnPlayerLevelChanged;

            SettingsService.Instance.ScavRepChanged -= OnScavRepChanged;
            SettingsService.Instance.ScavRepChanged += OnScavRepChanged;

            Refresh();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            QuestProgressService.Instance.ProgressChanged -= OnProgressChanged;
            SettingsService.Instance.PlayerLevelChanged -= OnPlayerLevelChanged;
            SettingsService.Instance.ScavRepChanged -= OnScavRepChanged;
        }

        private void OnProgressChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(Refresh);
        }

        private void OnPlayerLevelChanged(object? sender, int newLevel)
        {
            Dispatcher.InvokeAsync(Refresh);
        }

        private void OnScavRepChanged(object? sender, double newRep)
        {
            Dispatcher.InvokeAsync(Refresh);
        }

        public void Refresh()
        {
            _ = ViewModel.LoadLiveDashboardDataAsync();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        private void BtnViewAllTasks_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.NavigateToQuest(null!);
            }
        }

        private void BtnQuestTitle_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is UpNextTaskItem task)
            {
                NavigateToTask(task);
            }
        }

        private void BtnViewQuest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is UpNextTaskItem task)
            {
                NavigateToTask(task);
            }
        }

        private void NavigateToTask(UpNextTaskItem task)
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.NavigateToQuest(task.QuestId);
            }
        }

        private void TraderCardHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is TraderProgressCardViewModel card)
            {
                if (Application.Current.MainWindow is MainWindow mw)
                {
                    mw.NavigateToTrader(card.DisplayName);
                }
            }
        }

        private void BtnLoyaltyLevel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TraderProgressCardViewModel card &&
                btn.Tag is string tagStr && int.TryParse(tagStr, out int level))
            {
                ViewModel.SetTraderLoyaltyLevel(card.DisplayName, level);
            }
        }

        private void BtnScavKarmaMinus_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.AdjustScavKarma(-0.10);
        }

        private void BtnScavKarmaPlus_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.AdjustScavKarma(+0.10);
        }

        private void BtnBtrToggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ViewModel.ToggleBtrDriver();
        }

        private void KappaCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.NavigateToKappaQuests();
            }
        }
    }

    #region Value Converters

    public class BadgeBackgroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush UnlockTrader = new(Color.FromRgb(0x4A, 0x22, 0x22));
        private static readonly SolidColorBrush Opens = new(Color.FromRgb(0x1B, 0x3D, 0x28));
        private static readonly SolidColorBrush Lightkeeper = new(Color.FromRgb(0x3D, 0x2E, 0x15));
        private static readonly SolidColorBrush Kappa = new(Color.FromRgb(0x3D, 0x35, 0x12));
        private static readonly SolidColorBrush AlmostDone = new(Color.FromRgb(0x3D, 0x26, 0x12));
        private static readonly SolidColorBrush Ready = new(Color.FromRgb(0x1B, 0x2A, 0x3D));
        private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0x25, 0x25, 0x2C));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value as string)?.ToLowerInvariant() switch
            {
                "unlock-trader" => UnlockTrader,
                "opens" => Opens,
                "lightkeeper" => Lightkeeper,
                "kappa" => Kappa,
                "almost-done" => AlmostDone,
                "ready" => Ready,
                _ => DefaultBrush
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public class BadgeForegroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush UnlockTrader = new(Color.FromRgb(0xFF, 0x8A, 0x80));
        private static readonly SolidColorBrush Opens = new(Color.FromRgb(0x81, 0xC7, 0x84));
        private static readonly SolidColorBrush Lightkeeper = new(Color.FromRgb(0xFF, 0xD5, 0x4F));
        private static readonly SolidColorBrush Kappa = new(Color.FromRgb(0xF0, 0xD0, 0x60));
        private static readonly SolidColorBrush AlmostDone = new(Color.FromRgb(0xFF, 0xB7, 0x4D));
        private static readonly SolidColorBrush Ready = new(Color.FromRgb(0x64, 0xB5, 0xF6));
        private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0xD0, 0xD0, 0xD8));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value as string)?.ToLowerInvariant() switch
            {
                "unlock-trader" => UnlockTrader,
                "opens" => Opens,
                "lightkeeper" => Lightkeeper,
                "kappa" => Kappa,
                "almost-done" => AlmostDone,
                "ready" => Ready,
                _ => DefaultBrush
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public class BadgeBorderConverter : IValueConverter
    {
        private static readonly SolidColorBrush UnlockTrader = new(Color.FromRgb(0x8A, 0x33, 0x33));
        private static readonly SolidColorBrush Opens = new(Color.FromRgb(0x2E, 0x6A, 0x3E));
        private static readonly SolidColorBrush Lightkeeper = new(Color.FromRgb(0x6B, 0x4F, 0x1A));
        private static readonly SolidColorBrush Kappa = new(Color.FromRgb(0x6E, 0x5D, 0x1A));
        private static readonly SolidColorBrush AlmostDone = new(Color.FromRgb(0x6E, 0x3F, 0x1A));
        private static readonly SolidColorBrush Ready = new(Color.FromRgb(0x26, 0x4D, 0x73));
        private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0x3D, 0x3D, 0x48));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value as string)?.ToLowerInvariant() switch
            {
                "unlock-trader" => UnlockTrader,
                "opens" => Opens,
                "lightkeeper" => Lightkeeper,
                "kappa" => Kappa,
                "almost-done" => AlmostDone,
                "ready" => Ready,
                _ => DefaultBrush
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public class LoyaltyButtonBgConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush ActiveBg = new(Color.FromRgb(0xC5, 0xA8, 0x4A));
        private static readonly SolidColorBrush InactiveBg = new(Color.FromRgb(0x25, 0x25, 0x2C));

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 2 &&
                values[0] != null && values[1] != null &&
                int.TryParse(values[0].ToString(), out int current) &&
                int.TryParse(values[1].ToString(), out int target))
            {
                return current == target ? ActiveBg : InactiveBg;
            }
            return InactiveBg;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class LoyaltyButtonFgConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush ActiveFg = new(Colors.White);
        private static readonly SolidColorBrush InactiveFg = new(Color.FromRgb(0x9E, 0x9E, 0x9E));

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 2 &&
                values[0] != null && values[1] != null &&
                int.TryParse(values[0].ToString(), out int current) &&
                int.TryParse(values[1].ToString(), out int target))
            {
                return current == target ? ActiveFg : InactiveFg;
            }
            return InactiveFg;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class LoyaltyButtonBorderConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush ActiveBorder = new(Color.FromRgb(0xE5, 0xC8, 0x6A));
        private static readonly SolidColorBrush InactiveBorder = new(Color.FromRgb(0x3D, 0x3D, 0x48));

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 2 &&
                values[0] != null && values[1] != null &&
                int.TryParse(values[0].ToString(), out int current) &&
                int.TryParse(values[1].ToString(), out int target))
            {
                return current == target ? ActiveBorder : InactiveBorder;
            }
            return InactiveBorder;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    #endregion
}
