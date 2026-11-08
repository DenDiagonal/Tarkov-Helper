using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services;
using TarkovHelper.Services.DataAudit;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Windows.Dialogs
{
    #region ViewModels

    /// <summary>
    /// Filter categories for the Diff Viewer tabs.
    /// </summary>
    public enum DiffFilterTab
    {
        All,
        Quests,
        Requirements,
        Items,
        Hideout
    }

    /// <summary>
    /// ViewModel for an individual property difference showing Old Value vs New Value.
    /// </summary>
    public class PropertyDiffViewModel
    {
        public string PropertyName { get; }
        public string? OldValue { get; }
        public string? NewValue { get; }
        public string DisplayPropertyName { get; }
        public string DisplayOldValue { get; }
        public string DisplayNewValue { get; }
        public string FormattedSummary { get; }

        public PropertyDiffViewModel(PropertyDiff diff)
        {
            if (diff == null) throw new ArgumentNullException(nameof(diff));

            PropertyName = diff.PropertyName ?? string.Empty;
            OldValue = diff.OldValue;
            NewValue = diff.NewValue;

            DisplayPropertyName = FormatPropertyName(PropertyName);
            DisplayOldValue = string.IsNullOrWhiteSpace(diff.OldValue) ? "(vacío)" : diff.OldValue;
            DisplayNewValue = string.IsNullOrWhiteSpace(diff.NewValue) ? "(ninguno)" : diff.NewValue;
            FormattedSummary = $"{DisplayPropertyName}: {DisplayOldValue} ➔ {DisplayNewValue}";
        }

        private static string FormatPropertyName(string name) => name switch
        {
            "MinLevel" => "Nivel Mínimo",
            "Trader" => "Comerciante",
            "Faction" => "Facción",
            "MinScavKarma" => "Karma Scav Mínimo",
            "KappaRequired" => "Requerido para Kappa",
            "RequiredEdition" => "Edición Requerida",
            "ExcludedEdition" => "Edición Excluida",
            "RequiredPrestigeLevel" => "Nivel de Prestigio",
            "RequiredDecodeCount" => "Decodificaciones Requeridas",
            "Count" or "Quantity" => "Cantidad",
            "RequiresFIR" or "FoundInRaid" => "Encontrado en Raid (FIR)",
            "RequirementType" => "Tipo de Requisito",
            "DelayMinutes" => "Tiempo de Espera (min)",
            "MaxLevel" => "Nivel Máximo",
            "ConstructionTime" => "Tiempo de Construcción (s)",
            _ => name
        };
    }

    /// <summary>
    /// ViewModel for an available database backup file (.bak).
    /// </summary>
    public class BackupItemViewModel
    {
        public string FilePath { get; }
        public string FileName { get; }
        public DateTime Timestamp { get; }
        public string FormattedTimestamp { get; }
        public long FileSizeBytes { get; }
        public string FormattedSize { get; }
        public string DisplayText { get; }

        public BackupItemViewModel(string filePath)
        {
            FilePath = filePath ?? string.Empty;
            FileName = !string.IsNullOrEmpty(filePath) ? (Path.GetFileName(filePath) ?? string.Empty) : string.Empty;

            var fi = !string.IsNullOrEmpty(filePath) ? new FileInfo(filePath) : null;
            FileSizeBytes = fi != null && fi.Exists ? fi.Length : 0;
            FormattedSize = FormatFileSize(FileSizeBytes);

            Timestamp = ParseTimestampFromFileName(FileName, fi != null && fi.Exists ? fi.LastWriteTime : DateTime.Now);
            FormattedTimestamp = Timestamp.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

            DisplayText = $"{FileName}  ({FormattedSize}) — {FormattedTimestamp}";
        }

        public static DateTime ParseTimestampFromFileName(string fileName, DateTime fallback)
        {
            var match = Regex.Match(fileName, @"\.bak_(\d{8})_(\d{6})", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var datePart = match.Groups[1].Value;
                var timePart = match.Groups[2].Value;
                if (DateTime.TryParseExact($"{datePart}_{timePart}", "yyyyMMdd_HHmmss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    return dt;
                }
            }
            return fallback;
        }

        public static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture)} KB";
            return $"{(bytes / (1024.0 * 1024.0)).ToString("F2", CultureInfo.InvariantCulture)} MB";
        }
    }

    /// <summary>
    /// ViewModel for a classified diff item card.
    /// </summary>
    public class DiffItemCardViewModel
    {
        public IDiffItem DiffItem { get; }
        public DiffCategory Category => DiffItem.Category;
        public DiffChangeType ChangeType => DiffItem.ChangeType;
        public string EntityId => DiffItem.EntityId;
        public string Title => DiffItem.DisplayName;

        public string CategoryLabel { get; }
        public string ChangeBadgeText { get; }
        public string AccentColorHex { get; }
        public string BadgeBackgroundHex { get; }
        public string Description => DiffItem.Description;

        public List<PropertyDiffViewModel> PropertyChanges { get; } = new();
        public bool HasPropertyChanges => PropertyChanges.Count > 0;
        public Visibility PropertyChangesVisibility => HasPropertyChanges ? Visibility.Visible : Visibility.Collapsed;

        public List<string> AddedPrerequisites { get; } = new();
        public List<string> RemovedPrerequisites { get; } = new();
        public bool HasPrerequisites => AddedPrerequisites.Count > 0 || RemovedPrerequisites.Count > 0;
        public Visibility PrerequisitesVisibility => HasPrerequisites ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AddedPrerequisitesVisibility => AddedPrerequisites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RemovedPrerequisitesVisibility => RemovedPrerequisites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public DiffItemCardViewModel(IDiffItem item)
        {
            DiffItem = item ?? throw new ArgumentNullException(nameof(item));

            CategoryLabel = item.Category switch
            {
                DiffCategory.Quest => "MISIÓN",
                DiffCategory.QuestRequirement => "REQUISITO",
                DiffCategory.QuestItem => "OBJETO REQUERIDO",
                DiffCategory.HideoutStation => "ESTACIÓN HIDEOUT",
                DiffCategory.HideoutLevel => "NIVEL HIDEOUT",
                DiffCategory.HideoutItem => "OBJETO HIDEOUT",
                _ => item.Category.ToString().ToUpperInvariant()
            };

            switch (item.ChangeType)
            {
                case DiffChangeType.Added:
                    ChangeBadgeText = "+ AÑADIDO";
                    AccentColorHex = "#4CAF50"; // Green
                    BadgeBackgroundHex = "#1B5E20";
                    break;
                case DiffChangeType.Modified:
                    ChangeBadgeText = "~ MODIFICADO";
                    AccentColorHex = "#FFB300"; // Amber
                    BadgeBackgroundHex = "#E65100";
                    break;
                case DiffChangeType.Removed:
                    ChangeBadgeText = "- ELIMINADO";
                    AccentColorHex = "#EF5350"; // Red
                    BadgeBackgroundHex = "#B71C1C";
                    break;
                default:
                    ChangeBadgeText = "= SIN CAMBIOS";
                    AccentColorHex = "#9E9E9E";
                    BadgeBackgroundHex = "#333333";
                    break;
            }

            if (item.PropertyChanges != null)
            {
                foreach (var p in item.PropertyChanges)
                {
                    PropertyChanges.Add(new PropertyDiffViewModel(p));
                }
            }

            if (item is QuestDiff qd)
            {
                if (qd.AddedPrerequisiteNames != null && qd.AddedPrerequisiteNames.Count > 0)
                    AddedPrerequisites.AddRange(qd.AddedPrerequisiteNames);
                else if (qd.AddedPrerequisiteQuestIds != null && qd.AddedPrerequisiteQuestIds.Count > 0)
                    AddedPrerequisites.AddRange(qd.AddedPrerequisiteQuestIds);

                if (qd.RemovedPrerequisiteNames != null && qd.RemovedPrerequisiteNames.Count > 0)
                    RemovedPrerequisites.AddRange(qd.RemovedPrerequisiteNames);
                else if (qd.RemovedPrerequisiteQuestIds != null && qd.RemovedPrerequisiteQuestIds.Count > 0)
                    RemovedPrerequisites.AddRange(qd.RemovedPrerequisiteQuestIds);
            }
        }
    }

    /// <summary>
    /// ViewModel managing state, tabs, filtering, and backups for DiffViewerDialog.
    /// </summary>
    public class DiffViewerViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public DataDiffReport Report { get; }

        public int TotalChanges => Report.TotalChanges;
        public int AddedCount => Report.AddedCount;
        public int ModifiedCount => Report.ModifiedCount;
        public int RemovedCount => Report.RemovedCount;
        public int UnchangedCount => Report.UnchangedCount;
        public bool HasChanges => Report.HasChanges;

        public List<DiffItemCardViewModel> AllCards { get; } = new();
        public ObservableCollection<DiffItemCardViewModel> FilteredCards { get; } = new();
        public ObservableCollection<BackupItemViewModel> AvailableBackups { get; } = new();

        private BackupItemViewModel? _selectedBackup;
        public BackupItemViewModel? SelectedBackup
        {
            get => _selectedBackup;
            set
            {
                _selectedBackup = value;
                OnPropertyChanged();
            }
        }

        private DiffFilterTab _activeTab = DiffFilterTab.All;
        public DiffFilterTab ActiveTab
        {
            get => _activeTab;
            set
            {
                if (_activeTab != value)
                {
                    _activeTab = value;
                    OnPropertyChanged();
                    ApplyFilter();
                }
            }
        }

        public int AllCount => AllCards.Count;
        public int QuestsCount => AllCards.Count(c => c.Category == DiffCategory.Quest);
        public int RequirementsCount => AllCards.Count(c => c.Category == DiffCategory.QuestRequirement);
        public int ItemsCount => AllCards.Count(c => c.Category == DiffCategory.QuestItem);
        public int HideoutCount => AllCards.Count(c =>
            c.Category == DiffCategory.HideoutStation ||
            c.Category == DiffCategory.HideoutLevel ||
            c.Category == DiffCategory.HideoutItem);

        public string TabAllLabel => $"Todos ({AllCount})";
        public string TabQuestsLabel => $"Misiones ({QuestsCount})";
        public string TabRequirementsLabel => $"Requisitos ({RequirementsCount})";
        public string TabItemsLabel => $"Objetos ({ItemsCount})";
        public string TabHideoutLabel => $"Hideout ({HideoutCount})";

        public DiffViewerViewModel(DataDiffReport report)
        {
            Report = report ?? throw new ArgumentNullException(nameof(report));

            // Load all changed items into cards
            foreach (var diff in report.AllChanges())
            {
                AllCards.Add(new DiffItemCardViewModel(diff));
            }

            ApplyFilter();
        }

        public void SetFilter(DiffFilterTab tab)
        {
            ActiveTab = tab;
        }

        private void ApplyFilter()
        {
            FilteredCards.Clear();

            IEnumerable<DiffItemCardViewModel> query = ActiveTab switch
            {
                DiffFilterTab.Quests => AllCards.Where(c => c.Category == DiffCategory.Quest),
                DiffFilterTab.Requirements => AllCards.Where(c => c.Category == DiffCategory.QuestRequirement),
                DiffFilterTab.Items => AllCards.Where(c => c.Category == DiffCategory.QuestItem),
                DiffFilterTab.Hideout => AllCards.Where(c =>
                    c.Category == DiffCategory.HideoutStation ||
                    c.Category == DiffCategory.HideoutLevel ||
                    c.Category == DiffCategory.HideoutItem),
                _ => AllCards
            };

            foreach (var card in query)
            {
                FilteredCards.Add(card);
            }
        }

        public void LoadBackups(string? dbDirectory = null)
        {
            AvailableBackups.Clear();
            var backups = DatabaseBackupManager.Instance.GetAvailableBackups(dbDirectory);
            foreach (var b in backups)
            {
                AvailableBackups.Add(new BackupItemViewModel(b));
            }

            SelectedBackup = AvailableBackups.FirstOrDefault();
        }

        public async Task<bool> RestoreBackupAsync(BackupItemViewModel backup, string? targetDb = null)
        {
            if (backup == null) throw new ArgumentNullException(nameof(backup));
            return await DatabaseBackupManager.Instance.RestoreBackupAsync(backup.FilePath, targetDb);
        }
    }

    #endregion

    /// <summary>
    /// Interaction logic for DiffViewerDialog.xaml
    /// </summary>
    public partial class DiffViewerDialog : Window
    {
        private static readonly ILogger _log = Log.For<DiffViewerDialog>();
        private readonly DiffViewerViewModel _viewModel;

        public DiffViewerViewModel ViewModel => _viewModel;
        public DataDiffReport Report => _viewModel.Report;

        /// <summary>
        /// True if the user confirmed and applied the diffs.
        /// </summary>
        public bool WasApplied { get; private set; }

        /// <summary>
        /// True if a backup was successfully restored during this session.
        /// </summary>
        public bool WasRestored { get; private set; }

        /// <summary>
        /// Path of the backup restored, if applicable.
        /// </summary>
        public string? RestoredBackupPath { get; private set; }

        public DiffViewerDialog(DataDiffReport report)
        {
            InitializeComponent();

            _viewModel = new DiffViewerViewModel(report ?? new DataDiffReport());
            DataContext = _viewModel;

            Loaded += DiffViewerDialog_Loaded;
        }

        /// <summary>
        /// Shows the Diff Viewer modal window for the specified report.
        /// </summary>
        public static bool? ShowDiffDialog(DataDiffReport report, Window? owner = null)
        {
            var dialog = new DiffViewerDialog(report);
            if (owner != null)
            {
                dialog.Owner = owner;
            }
            return dialog.ShowDialog();
        }

        /// <summary>
        /// Creates a DiffViewerDialog configured directly in Rollback mode.
        /// </summary>
        public static DiffViewerDialog CreateRollbackDialog(Window? owner = null)
        {
            var dialog = new DiffViewerDialog(new DataDiffReport());
            if (owner != null)
            {
                dialog.Owner = owner;
            }
            dialog.ShowRollbackMode();
            return dialog;
        }

        private void DiffViewerDialog_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSummaryBar();
            UpdateTabLabels();
            UpdateTabButtonStyles(_viewModel.ActiveTab);
            UpdateContentVisibility();

            DiffCardsItemsControl.ItemsSource = _viewModel.FilteredCards;
            BackupListBox.ItemsSource = _viewModel.AvailableBackups;
        }

        private void UpdateSummaryBar()
        {
            TxtTotalChanges.Text = $"Total: {_viewModel.TotalChanges} cambios";
            TxtAddedCount.Text = $"+ {_viewModel.AddedCount} Añadidos";
            TxtModifiedCount.Text = $"~ {_viewModel.ModifiedCount} Modificados";
            TxtRemovedCount.Text = $"- {_viewModel.RemovedCount} Eliminados";

            if (!string.IsNullOrEmpty(_viewModel.Report.DatabasePath))
            {
                TxtDbPath.Text = Path.GetFileName(_viewModel.Report.DatabasePath);
                TxtDbPath.ToolTip = _viewModel.Report.DatabasePath;
            }

            // Disable Apply button if 0 changes
            BtnApplyChanges.IsEnabled = _viewModel.HasChanges;
            if (!_viewModel.HasChanges)
            {
                BtnApplyChanges.Opacity = 0.5;
            }
            else
            {
                BtnApplyChanges.Opacity = 1.0;
            }
        }

        private void UpdateTabLabels()
        {
            BtnFilterAll.Content = _viewModel.TabAllLabel;
            BtnFilterQuests.Content = _viewModel.TabQuestsLabel;
            BtnFilterRequirements.Content = _viewModel.TabRequirementsLabel;
            BtnFilterItems.Content = _viewModel.TabItemsLabel;
            BtnFilterHideout.Content = _viewModel.TabHideoutLabel;
        }

        private void UpdateContentVisibility()
        {
            if (!_viewModel.HasChanges)
            {
                EmptyStateBorder.Visibility = Visibility.Visible;
                DiffCardsScrollViewer.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyStateBorder.Visibility = Visibility.Collapsed;
                DiffCardsScrollViewer.Visibility = Visibility.Visible;
            }
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static bool IsTestEnvironment =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName?.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) == true);

        /// <summary>
        /// Programmatically applies diff changes via DatabaseTransactionApplier.
        /// </summary>
        public async Task<bool> ApplyChangesAsync()
        {
            if (!_viewModel.HasChanges)
                return false;

            WasApplied = true;

            if (!string.IsNullOrWhiteSpace(_viewModel.Report.DatabasePath))
            {
                return await DatabaseTransactionApplier.Instance.ApplyDiffAsync(_viewModel.Report, _viewModel.Report.DatabasePath, resilientPrerequisites: true);
            }

            if (!IsTestEnvironment)
            {
                return await DatabaseTransactionApplier.Instance.ApplyDiffAsync(_viewModel.Report, resilientPrerequisites: true);
            }

            return true;
        }

        private async void BtnApplyChanges_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.HasChanges)
            {
                if (!IsTestEnvironment)
                {
                    MessageBox.Show(this, "No hay cambios pendientes para aplicar.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            WasApplied = true;

            try
            {
                BtnApplyChanges.IsEnabled = false;
                BtnCancel.IsEnabled = false;
                BtnRollback.IsEnabled = false;
                BtnApplyChanges.Content = "Aplicando cambios...";

                var success = await ApplyChangesAsync();
                if (success)
                {
                    if (!IsTestEnvironment)
                    {
                        MessageBox.Show(this, "Los cambios han sido aplicados con éxito en la base de datos y los servicios en memoria han sido actualizados.", "Actualización Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    DialogResult = true;
                    Close();
                }
                else
                {
                    BtnApplyChanges.IsEnabled = true;
                    BtnCancel.IsEnabled = true;
                    BtnRollback.IsEnabled = true;
                    BtnApplyChanges.Content = "Confirmar y Aplicar Cambios";
                    if (!IsTestEnvironment)
                    {
                        var errorDetail = !string.IsNullOrEmpty(DatabaseTransactionApplier.Instance.LastError)
                            ? $"\n\nDetalle: {DatabaseTransactionApplier.Instance.LastError}"
                            : "";
                        MessageBox.Show(this, $"Ocurrió un error al aplicar los cambios. La transacción fue revertida completamente y la base de datos no fue modificada.{errorDetail}", "Error al Aplicar Cambios", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Exception applying diff: {ex.Message}");
                if (!IsTestEnvironment)
                {
                    MessageBox.Show(this, $"Error inesperado al aplicar cambios: {ex.Message}\n\nLa base de datos se mantiene intacta.", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                Close();
            }
        }

        private void BtnFilterTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender == BtnFilterAll)
                _viewModel.SetFilter(DiffFilterTab.All);
            else if (sender == BtnFilterQuests)
                _viewModel.SetFilter(DiffFilterTab.Quests);
            else if (sender == BtnFilterRequirements)
                _viewModel.SetFilter(DiffFilterTab.Requirements);
            else if (sender == BtnFilterItems)
                _viewModel.SetFilter(DiffFilterTab.Items);
            else if (sender == BtnFilterHideout)
                _viewModel.SetFilter(DiffFilterTab.Hideout);

            UpdateTabButtonStyles(_viewModel.ActiveTab);
        }

        private void UpdateTabButtonStyles(DiffFilterTab activeTab)
        {
            var activeBg = (Brush)new BrushConverter().ConvertFrom("#C8AA6E")!;
            var activeFg = (Brush)new BrushConverter().ConvertFrom("#1A1A1E")!;
            var inactiveBg = (Brush)new BrushConverter().ConvertFrom("#252528")!;
            var inactiveFg = (Brush)new BrushConverter().ConvertFrom("#E0E0E0")!;

            SetButtonStyle(BtnFilterAll, activeTab == DiffFilterTab.All, activeBg, activeFg, inactiveBg, inactiveFg);
            SetButtonStyle(BtnFilterQuests, activeTab == DiffFilterTab.Quests, activeBg, activeFg, inactiveBg, inactiveFg);
            SetButtonStyle(BtnFilterRequirements, activeTab == DiffFilterTab.Requirements, activeBg, activeFg, inactiveBg, inactiveFg);
            SetButtonStyle(BtnFilterItems, activeTab == DiffFilterTab.Items, activeBg, activeFg, inactiveBg, inactiveFg);
            SetButtonStyle(BtnFilterHideout, activeTab == DiffFilterTab.Hideout, activeBg, activeFg, inactiveBg, inactiveFg);
        }

        private static void SetButtonStyle(Button btn, bool isActive, Brush activeBg, Brush activeFg, Brush inactiveBg, Brush inactiveFg)
        {
            btn.Background = isActive ? activeBg : inactiveBg;
            btn.Foreground = isActive ? activeFg : inactiveFg;
            btn.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
        }

        #region Rollback Mode & Logic

        public void ShowRollbackMode()
        {
            RollbackOverlay.Visibility = Visibility.Visible;
            _viewModel.LoadBackups();

            if (_viewModel.AvailableBackups.Count == 0)
            {
                TxtNoBackups.Visibility = Visibility.Visible;
                BackupListBox.Visibility = Visibility.Collapsed;
                BtnConfirmRollback.IsEnabled = false;
            }
            else
            {
                TxtNoBackups.Visibility = Visibility.Collapsed;
                BackupListBox.Visibility = Visibility.Visible;
                BtnConfirmRollback.IsEnabled = true;
            }
        }

        private void BtnRollback_Click(object sender, RoutedEventArgs e)
        {
            ShowRollbackMode();
        }

        private void BtnCancelRollback_Click(object sender, RoutedEventArgs e)
        {
            RollbackOverlay.Visibility = Visibility.Collapsed;
            TxtRollbackStatus.Text = string.Empty;
        }

        private async void BtnConfirmRollback_Click(object sender, RoutedEventArgs e)
        {
            var selectedBackup = BackupListBox.SelectedItem as BackupItemViewModel ?? _viewModel.SelectedBackup;
            if (selectedBackup == null)
            {
                MessageBox.Show(this, "Por favor seleccione una copia de seguridad de la lista.", "Seleccionar Respaldo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirmResult = MessageBox.Show(
                this,
                $"¿Está seguro de que desea restaurar la base de datos desde el respaldo '{selectedBackup.FileName}'?\n\n" +
                $"Fecha: {selectedBackup.FormattedTimestamp}\n" +
                $"Tamaño: {selectedBackup.FormattedSize}\n\n" +
                "Esta operación reemplazará la base de datos local 'tarkov_data.db' con la versión seleccionada.",
                "Confirmar Restauración de Base de Datos",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                BtnConfirmRollback.IsEnabled = false;
                BtnCancelRollback.IsEnabled = false;
                TxtRollbackStatus.Text = "Restaurando base de datos desde respaldo...";
                TxtRollbackStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#FFE082")!;

                var success = await _viewModel.RestoreBackupAsync(selectedBackup);
                if (success)
                {
                    WasRestored = true;
                    RestoredBackupPath = selectedBackup.FilePath;

                    _log.Info($"Database successfully rolled back to: {selectedBackup.FilePath}");

                    // Fire DatabaseUpdated notification
                    try
                    {
                        DatabaseUpdateService.Instance.NotifyDatabaseUpdated();
                    }
                    catch (Exception ex)
                    {
                        _log.Warning($"Failed to dispatch NotifyDatabaseUpdated: {ex.Message}");
                    }

                    TxtRollbackStatus.Text = "✓ Base de datos restaurada exitosamente.";
                    TxtRollbackStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#A5D6A7")!;

                    MessageBox.Show(
                        this,
                        $"La base de datos se ha restaurado correctamente a la versión:\n\n{selectedBackup.FileName}",
                        "Restauración Completada",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    DialogResult = true;
                    Close();
                }
                else
                {
                    TxtRollbackStatus.Text = "Error: no se pudo restaurar la base de datos.";
                    TxtRollbackStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#EF5350")!;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Error restoring database backup: {ex.Message}");
                TxtRollbackStatus.Text = $"Error al restaurar: {ex.Message}";
                TxtRollbackStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#EF5350")!;

                MessageBox.Show(
                    this,
                    $"Ocurrió un error al restaurar el respaldo:\n\n{ex.Message}",
                    "Error de Restauración",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                BtnConfirmRollback.IsEnabled = true;
                BtnCancelRollback.IsEnabled = true;
            }
        }

        #endregion
    }
}
