using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Windows.Dialogs;
using Xunit;

namespace TarkovHelper.Tests
{
    /// <summary>
    /// Empirical adversarial stress tests for DiffViewerDialog and its ViewModels.
    /// Stress-tests boundary inputs, massive data sets, nulls, rapid tab cycling, and dialog cancelation.
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class DiffViewerAdversarialTests
    {
        #region Helper STA Runner

        private static void RunOnStaThread(Action<DiffViewerDialog> testAction, DataDiffReport? report = null)
        {
            Exception? threadException = null;
            var thread = new Thread(() =>
            {
                DiffViewerDialog? dialog = null;
                try
                {
                    dialog = new DiffViewerDialog(report ?? new DataDiffReport());
                    dialog.Loaded += (s, e) =>
                    {
                        try
                        {
                            testAction(dialog);
                        }
                        catch (Exception ex)
                        {
                            threadException = ex;
                        }
                        finally
                        {
                            try { dialog.Close(); } catch { }
                        }
                    };

                    dialog.ShowDialog();
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
                finally
                {
                    if (dialog != null)
                    {
                        try { dialog.Close(); } catch { }
                    }
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            bool finished = thread.Join(TimeSpan.FromSeconds(15));
            if (!finished)
            {
                thread.Interrupt();
                Assert.Fail("STA test timed out after 15 seconds.");
            }

            if (threadException != null)
            {
                throw new AggregateException("Adversarial STA execution failed", threadException);
            }
        }

        private static DataDiffReport CreateMassiveDiffReport(int totalItems = 1250)
        {
            var report = new DataDiffReport();

            int questCount = (int)(totalItems * 0.36); // ~450
            int reqCount = (int)(totalItems * 0.24);   // ~300
            int itemCount = (int)(totalItems * 0.24);  // ~300
            int hideoutCount = totalItems - questCount - reqCount - itemCount; // ~200

            // 1. Quests
            for (int i = 0; i < questCount; i++)
            {
                var changeType = (i % 3) switch
                {
                    0 => DiffChangeType.Added,
                    1 => DiffChangeType.Modified,
                    _ => DiffChangeType.Removed
                };

                var qd = new QuestDiff
                {
                    QuestId = $"q_{i:D4}",
                    QuestName = $"Adversarial Quest #{i}",
                    ChangeType = changeType,
                    NewTrader = "Prapor",
                    OldMinLevel = 10,
                    NewMinLevel = 15 + (i % 20)
                };

                if (changeType == DiffChangeType.Modified)
                {
                    qd.PropertyChanges.Add(new PropertyDiff("MinLevel", "10", (15 + (i % 20)).ToString()));
                    qd.PropertyChanges.Add(new PropertyDiff("Faction", "Any", "USEC"));
                    qd.AddedPrerequisiteNames.Add($"Prereq Quest Alpha {i}");
                    qd.RemovedPrerequisiteNames.Add($"Prereq Quest Beta {i}");
                }

                report.QuestDiffs.Add(qd);
            }

            // 2. Requirements
            for (int i = 0; i < reqCount; i++)
            {
                report.QuestRequirementDiffs.Add(new QuestRequirementDiff
                {
                    RequirementId = $"req_{i:D4}",
                    QuestId = $"q_{i % 50:D4}",
                    QuestName = $"Parent Quest {i}",
                    RequiredQuestId = $"q_{(i + 1) % 50:D4}",
                    RequiredQuestName = $"Required Quest {i}",
                    ChangeType = (i % 2 == 0) ? DiffChangeType.Added : DiffChangeType.Removed,
                    NewRequirementType = "Complete"
                });
            }

            // 3. Quest Items
            for (int i = 0; i < itemCount; i++)
            {
                var itemDiff = new QuestItemDiff
                {
                    RequirementId = $"item_{i:D4}",
                    QuestId = $"q_{i % 50:D4}",
                    QuestName = $"Quest Item Host {i}",
                    ItemName = $"Tactical Battery #{i}",
                    ChangeType = DiffChangeType.Modified,
                    OldQuantity = 1,
                    NewQuantity = 3,
                    OldRequiresFIR = false,
                    NewRequiresFIR = true
                };
                itemDiff.PropertyChanges.Add(new PropertyDiff("Count", "1", "3"));
                itemDiff.PropertyChanges.Add(new PropertyDiff("RequiresFIR", "False", "True"));
                report.QuestItemDiffs.Add(itemDiff);
            }

            // 4. Hideout
            for (int i = 0; i < hideoutCount; i++)
            {
                if (i % 2 == 0)
                {
                    var hl = new HideoutLevelDiff
                    {
                        LevelId = $"hl_{i:D4}",
                        StationName = $"Station {i % 10}",
                        Level = (i % 3) + 1,
                        ChangeType = DiffChangeType.Modified,
                        OldConstructionTime = 1200,
                        NewConstructionTime = 2400
                    };
                    hl.PropertyChanges.Add(new PropertyDiff("ConstructionTime", "1200", "2400"));
                    report.HideoutLevelDiffs.Add(hl);
                }
                else
                {
                    var hi = new HideoutItemDiff
                    {
                        RequirementId = $"hitem_{i:D4}",
                        StationName = $"Station {i % 10}",
                        Level = (i % 3) + 1,
                        ItemName = $"Bolts Pack {i}",
                        ChangeType = DiffChangeType.Added,
                        NewQuantity = 5
                    };
                    report.HideoutItemDiffs.Add(hi);
                }
            }

            return report;
        }

        #endregion

        #region Category 1: Empty Diff Report & Null Safety

        [Fact]
        public void Adversarial_EmptyReport_RendersEmptyState_AndDisablesApply()
        {
            var report = new DataDiffReport();

            RunOnStaThread(dialog =>
            {
                var emptyBorder = dialog.FindName("EmptyStateBorder") as Border;
                var scrollViewer = dialog.FindName("DiffCardsScrollViewer") as ScrollViewer;
                var btnApply = dialog.FindName("BtnApplyChanges") as Button;
                var txtTotal = dialog.FindName("TxtTotalChanges") as TextBlock;
                var txtAdded = dialog.FindName("TxtAddedCount") as TextBlock;
                var txtModified = dialog.FindName("TxtModifiedCount") as TextBlock;
                var txtRemoved = dialog.FindName("TxtRemovedCount") as TextBlock;

                Assert.NotNull(emptyBorder);
                Assert.NotNull(scrollViewer);
                Assert.NotNull(btnApply);
                Assert.NotNull(txtTotal);

                // 1. Verify Empty State is visible and Cards ScrollViewer is Collapsed
                Assert.Equal(Visibility.Visible, emptyBorder.Visibility);
                Assert.Equal(Visibility.Collapsed, scrollViewer.Visibility);

                // 2. Verify summary text and counts
                Assert.Equal("Total: 0 cambios", txtTotal.Text);
                Assert.Equal("+ 0 Añadidos", txtAdded!.Text);
                Assert.Equal("~ 0 Modificados", txtModified!.Text);
                Assert.Equal("- 0 Eliminados", txtRemoved!.Text);

                // 3. Verify Empty State text content
                var stackPanel = emptyBorder.Child as StackPanel;
                Assert.NotNull(stackPanel);
                var textBlocks = stackPanel.Children.OfType<TextBlock>().ToList();
                Assert.True(textBlocks.Count >= 3);
                Assert.Equal("✓", textBlocks[0].Text);
                Assert.Contains("La base de datos local está sincronizada", textBlocks[1].Text);
                Assert.Contains("coinciden exactamente con las fuentes remotas auditadas", textBlocks[2].Text);

                // 4. Verify Apply button disabled state
                Assert.False(btnApply.IsEnabled);
                Assert.Equal(0.5, btnApply.Opacity);

                // 5. Verify ViewModel values
                Assert.False(dialog.ViewModel.HasChanges);
                Assert.Equal(0, dialog.ViewModel.TotalChanges);
                Assert.False(dialog.WasApplied);
                Assert.False(dialog.WasRestored);
            }, report);
        }

        [Fact]
        public void Adversarial_NullReport_GracefullyInstantiatesEmptyReport()
        {
            RunOnStaThread(dialog =>
            {
                Assert.NotNull(dialog.ViewModel);
                Assert.NotNull(dialog.Report);
                Assert.False(dialog.ViewModel.HasChanges);
                Assert.Equal(0, dialog.ViewModel.TotalChanges);

                var emptyBorder = dialog.FindName("EmptyStateBorder") as Border;
                Assert.NotNull(emptyBorder);
                Assert.Equal(Visibility.Visible, emptyBorder.Visibility);
            }, null);
        }

        [Fact]
        public void Adversarial_EntitiesWithNullsAndCorruptedValues_HandledSafely()
        {
            var report = new DataDiffReport();

            // Adversarial QuestDiff with nulls
            report.QuestDiffs.Add(new QuestDiff
            {
                QuestId = null!,
                QuestName = null!,
                ChangeType = DiffChangeType.Modified,
                OldTrader = null,
                NewTrader = null,
                OldMinLevel = null,
                NewMinLevel = null,
                AddedPrerequisiteNames = new List<string> { null!, "" },
                RemovedPrerequisiteNames = new List<string> { null!, "OldPrereq" },
                PropertyChanges = new List<PropertyDiff>
                {
                    new PropertyDiff(null!, null, null),
                    new PropertyDiff("CustomUnknownProperty123", "", "   "),
                    new PropertyDiff("MinLevel", new string('X', 5000), new string('Y', 5000))
                }
            });

            // Adversarial QuestRequirementDiff with nulls
            report.QuestRequirementDiffs.Add(new QuestRequirementDiff
            {
                RequirementId = null!,
                QuestName = null!,
                RequiredQuestName = null!,
                ChangeType = DiffChangeType.Added,
                NewRequirementType = null
            });

            // Adversarial QuestItemDiff with nulls
            report.QuestItemDiffs.Add(new QuestItemDiff
            {
                RequirementId = null!,
                ItemName = null!,
                QuestName = null!,
                ChangeType = DiffChangeType.Removed
            });

            // Adversarial HideoutStationDiff with nulls
            report.HideoutStationDiffs.Add(new HideoutStationDiff
            {
                StationId = null!,
                StationName = null!,
                ChangeType = DiffChangeType.Added
            });

            // Adversarial HideoutLevelDiff with nulls
            report.HideoutLevelDiffs.Add(new HideoutLevelDiff
            {
                LevelId = null!,
                StationName = null!,
                ChangeType = DiffChangeType.Modified
            });

            // Adversarial HideoutItemDiff with nulls
            report.HideoutItemDiffs.Add(new HideoutItemDiff
            {
                RequirementId = null!,
                ItemName = null!,
                StationName = null!,
                ChangeType = DiffChangeType.Added
            });

            // 1. ViewModel creation must not throw NRE
            var vm = new DiffViewerViewModel(report);
            Assert.Equal(6, vm.TotalChanges);
            Assert.Equal(6, vm.AllCards.Count);

            // 2. Validate property diff viewmodel formatting under nulls
            var card0 = vm.AllCards[0];
            Assert.Equal(3, card0.PropertyChanges.Count);

            var prop0 = card0.PropertyChanges[0];
            Assert.Equal(string.Empty, prop0.PropertyName);
            Assert.Equal("(vacío)", prop0.DisplayOldValue);
            Assert.Equal("(ninguno)", prop0.DisplayNewValue);
            Assert.False(string.IsNullOrEmpty(prop0.FormattedSummary));

            var prop1 = card0.PropertyChanges[1];
            Assert.Equal("CustomUnknownProperty123", prop1.DisplayPropertyName);
            Assert.Equal("(vacío)", prop1.DisplayOldValue);
            Assert.Equal("(ninguno)", prop1.DisplayNewValue);

            // 3. STA Dialog rendering must not throw
            RunOnStaThread(dialog =>
            {
                var scrollViewer = dialog.FindName("DiffCardsScrollViewer") as ScrollViewer;
                Assert.NotNull(scrollViewer);
                Assert.Equal(Visibility.Visible, scrollViewer.Visibility);
                Assert.Equal(6, dialog.ViewModel.FilteredCards.Count);
            }, report);
        }

        #endregion

        #region Category 2: Massive Diff Report (1,000+ Items) Stress Test

        [Fact]
        public void Adversarial_MassiveReport_1250Items_InstantiatesAndRendersWithinPerformanceThreshold()
        {
            var report = CreateMassiveDiffReport(1250);

            // Benchmark ViewModel creation
            var sw = Stopwatch.StartNew();
            var vm = new DiffViewerViewModel(report);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 500, $"ViewModel creation took too long: {sw.ElapsedMilliseconds} ms");
            Assert.Equal(1250, vm.TotalChanges);
            Assert.Equal(1250, vm.AllCount);
            Assert.Equal(450, vm.QuestsCount);
            Assert.Equal(300, vm.RequirementsCount);
            Assert.Equal(300, vm.ItemsCount);
            Assert.Equal(200, vm.HideoutCount);

            // STA dialog rendering benchmark
            RunOnStaThread(dialog =>
            {
                var emptyBorder = dialog.FindName("EmptyStateBorder") as Border;
                var scrollViewer = dialog.FindName("DiffCardsScrollViewer") as ScrollViewer;
                var btnApply = dialog.FindName("BtnApplyChanges") as Button;
                var txtTotal = dialog.FindName("TxtTotalChanges") as TextBlock;

                var btnFilterAll = dialog.FindName("BtnFilterAll") as Button;
                var btnFilterQuests = dialog.FindName("BtnFilterQuests") as Button;
                var btnFilterRequirements = dialog.FindName("BtnFilterRequirements") as Button;
                var btnFilterItems = dialog.FindName("BtnFilterItems") as Button;
                var btnFilterHideout = dialog.FindName("BtnFilterHideout") as Button;

                Assert.NotNull(emptyBorder);
                Assert.NotNull(scrollViewer);
                Assert.NotNull(btnApply);
                Assert.NotNull(txtTotal);

                Assert.Equal(Visibility.Collapsed, emptyBorder.Visibility);
                Assert.Equal(Visibility.Visible, scrollViewer.Visibility);

                Assert.Equal("Total: 1250 cambios", txtTotal.Text);
                Assert.True(btnApply.IsEnabled);
                Assert.Equal(1.0, btnApply.Opacity);

                Assert.Equal("Todos (1250)", btnFilterAll!.Content);
                Assert.Equal("Misiones (450)", btnFilterQuests!.Content);
                Assert.Equal("Requisitos (300)", btnFilterRequirements!.Content);
                Assert.Equal("Objetos (300)", btnFilterItems!.Content);
                Assert.Equal("Hideout (200)", btnFilterHideout!.Content);

                Assert.Equal(1250, dialog.ViewModel.FilteredCards.Count);
            }, report);
        }

        #endregion

        #region Category 3: Rapid Category Tab Switching Stress Test

        [Fact]
        public void Adversarial_RapidCategoryTabSwitching_100SwitchesUnderMassiveLoad()
        {
            var report = CreateMassiveDiffReport(1250);

            RunOnStaThread(dialog =>
            {
                var vm = dialog.ViewModel;
                var btnAll = dialog.FindName("BtnFilterAll") as Button;
                var btnQuests = dialog.FindName("BtnFilterQuests") as Button;
                var btnReqs = dialog.FindName("BtnFilterRequirements") as Button;
                var btnItems = dialog.FindName("BtnFilterItems") as Button;
                var btnHideout = dialog.FindName("BtnFilterHideout") as Button;

                Assert.NotNull(btnAll);
                Assert.NotNull(btnQuests);
                Assert.NotNull(btnReqs);
                Assert.NotNull(btnItems);
                Assert.NotNull(btnHideout);

                var sw = Stopwatch.StartNew();

                // Rapidly cycle across all 5 tabs 20 times (100 total tab transitions)
                for (int cycle = 0; cycle < 20; cycle++)
                {
                    // 1. Quests
                    btnQuests.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(DiffFilterTab.Quests, vm.ActiveTab);
                    Assert.Equal(450, vm.FilteredCards.Count);
                    Assert.Equal(FontWeights.Bold, btnQuests.FontWeight);
                    Assert.Equal(FontWeights.Normal, btnAll.FontWeight);

                    // 2. Requirements
                    btnReqs.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(DiffFilterTab.Requirements, vm.ActiveTab);
                    Assert.Equal(300, vm.FilteredCards.Count);
                    Assert.Equal(FontWeights.Bold, btnReqs.FontWeight);

                    // 3. Items
                    btnItems.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(DiffFilterTab.Items, vm.ActiveTab);
                    Assert.Equal(300, vm.FilteredCards.Count);
                    Assert.Equal(FontWeights.Bold, btnItems.FontWeight);

                    // 4. Hideout
                    btnHideout.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(DiffFilterTab.Hideout, vm.ActiveTab);
                    Assert.Equal(200, vm.FilteredCards.Count);
                    Assert.Equal(FontWeights.Bold, btnHideout.FontWeight);

                    // 5. All
                    btnAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(DiffFilterTab.All, vm.ActiveTab);
                    Assert.Equal(1250, vm.FilteredCards.Count);
                    Assert.Equal(FontWeights.Bold, btnAll.FontWeight);
                }

                sw.Stop();

                // 100 tab switches should execute well under 2 seconds
                Assert.True(sw.ElapsedMilliseconds < 2000,
                    $"100 rapid tab switches took {sw.ElapsedMilliseconds} ms, exceeding threshold");
            }, report);
        }

        #endregion

        #region Category 4: Dialog Cancelation and Rollback Lifecycle

        [Fact]
        public void Adversarial_DialogCancelation_ViaCancelButton_LeavesDatabaseUntouched()
        {
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        QuestId = "q_cancel",
                        QuestName = "Cancel Test Quest",
                        ChangeType = DiffChangeType.Added
                    }
                }
            };

            RunOnStaThread(dialog =>
            {
                var btnCancel = dialog.FindName("BtnCancel") as Button;
                Assert.NotNull(btnCancel);

                // Act: Click Cancel button
                btnCancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                // Assert: Dialog marked as not applied, not restored
                Assert.False(dialog.WasApplied);
                Assert.False(dialog.WasRestored);
            }, report);
        }

        [Fact]
        public void Adversarial_DialogCancelation_ViaCloseButton_LeavesDatabaseUntouched()
        {
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        QuestId = "q_close",
                        QuestName = "Close Test Quest",
                        ChangeType = DiffChangeType.Added
                    }
                }
            };

            RunOnStaThread(dialog =>
            {
                var btnClose = dialog.FindName("BtnClose") as Button;
                Assert.NotNull(btnClose);

                // Act: Click X close button
                btnClose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.False(dialog.WasApplied);
                Assert.False(dialog.WasRestored);
            }, report);
        }

        [Fact]
        public void Adversarial_RollbackMode_OpenAndCancel_RestoresDiffCardsView()
        {
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        QuestId = "q_rb",
                        QuestName = "Rollback Modal Quest",
                        ChangeType = DiffChangeType.Added
                    }
                }
            };

            RunOnStaThread(dialog =>
            {
                var rollbackOverlay = dialog.FindName("RollbackOverlay") as Border;
                var btnRollback = dialog.FindName("BtnRollback") as Button;
                var btnCancelRollback = dialog.FindName("BtnCancelRollback") as Button;
                var txtRollbackStatus = dialog.FindName("TxtRollbackStatus") as TextBlock;

                Assert.NotNull(rollbackOverlay);
                Assert.NotNull(btnRollback);
                Assert.NotNull(btnCancelRollback);
                Assert.NotNull(txtRollbackStatus);

                // Initially rollback overlay is collapsed
                Assert.Equal(Visibility.Collapsed, rollbackOverlay.Visibility);

                // 1. Open Rollback mode
                btnRollback.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, rollbackOverlay.Visibility);

                // 2. Cancel Rollback mode
                btnCancelRollback.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Collapsed, rollbackOverlay.Visibility);
                Assert.Equal(string.Empty, txtRollbackStatus.Text);

                // State integrity remains intact
                Assert.False(dialog.WasApplied);
                Assert.False(dialog.WasRestored);
            }, report);
        }

        [Fact]
        public void Adversarial_RollbackMode_ShowRollbackMode_LoadsAvailableBackupsAndConfiguresControls()
        {
            RunOnStaThread(dialog =>
            {
                var rollbackOverlay = dialog.FindName("RollbackOverlay") as Border;
                var txtNoBackups = dialog.FindName("TxtNoBackups") as TextBlock;
                var backupListBox = dialog.FindName("BackupListBox") as ListBox;
                var btnConfirmRollback = dialog.FindName("BtnConfirmRollback") as Button;

                Assert.NotNull(rollbackOverlay);
                Assert.NotNull(txtNoBackups);
                Assert.NotNull(backupListBox);
                Assert.NotNull(btnConfirmRollback);

                dialog.ShowRollbackMode();
                Assert.Equal(Visibility.Visible, rollbackOverlay.Visibility);

                // In the application repository, existing backup files are discovered in Assets/
                if (dialog.ViewModel.AvailableBackups.Count > 0)
                {
                    Assert.Equal(Visibility.Collapsed, txtNoBackups.Visibility);
                    Assert.Equal(Visibility.Visible, backupListBox.Visibility);
                    Assert.True(btnConfirmRollback.IsEnabled);
                    Assert.NotNull(dialog.ViewModel.SelectedBackup);
                }
                else
                {
                    Assert.Equal(Visibility.Visible, txtNoBackups.Visibility);
                    Assert.Equal(Visibility.Collapsed, backupListBox.Visibility);
                    Assert.False(btnConfirmRollback.IsEnabled);
                }
            });
        }

        [Fact]
        public void Adversarial_DialogApplyChanges_WhenHasChanges_SetsWasAppliedTrue()
        {
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        QuestId = "q_apply",
                        QuestName = "Apply Test Quest",
                        ChangeType = DiffChangeType.Added
                    }
                }
            };

            RunOnStaThread(dialog =>
            {
                var btnApply = dialog.FindName("BtnApplyChanges") as Button;
                Assert.NotNull(btnApply);
                Assert.True(btnApply.IsEnabled);

                // Act: Click Apply button
                btnApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                // Assert: WasApplied set to true
                Assert.True(dialog.WasApplied);
                Assert.False(dialog.WasRestored);
            }, report);
        }

        #endregion

        #region Category 5: Malformed and Boundary Backup Files

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("invalid_backup_file.txt")]
        [InlineData("tarkov_data.db.bak_invaliddate")]
        [InlineData("tarkov_data.db.bak_99999999_999999")]
        public void Adversarial_BackupItemViewModel_BoundaryAndMalformedInputs_NeverThrows(string? filePath)
        {
            var vm = new BackupItemViewModel(filePath!);

            Assert.NotNull(vm.FilePath);
            Assert.NotNull(vm.FileName);
            Assert.NotNull(vm.FormattedTimestamp);
            Assert.NotNull(vm.FormattedSize);
            Assert.NotNull(vm.DisplayText);
            Assert.Equal(0, vm.FileSizeBytes);
        }

        [Fact]
        public void Adversarial_BackupItemViewModel_WhitespaceInput_ThrowsArgumentException_ConfirmingBug()
        {
            // Finding: BackupItemViewModel checks string.IsNullOrEmpty(filePath) instead of
            // string.IsNullOrWhiteSpace(filePath) before calling new FileInfo(filePath).
            // This test empirically documents this vulnerability.
            Assert.Throws<ArgumentException>(() => new BackupItemViewModel("   "));
        }

        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(500, "500 B")]
        [InlineData(1024, "1.0 KB")]
        [InlineData(1536, "1.5 KB")]
        [InlineData(1048576, "1.00 MB")]
        [InlineData(52428800, "50.00 MB")]
        [InlineData(1073741824, "1024.00 MB")]
        public void Adversarial_BackupItemViewModel_FormatFileSize_HandlesAllScales(long bytes, string expected)
        {
            var formatted = BackupItemViewModel.FormatFileSize(bytes);
            Assert.Equal(expected, formatted);
        }

        [Fact]
        public void Adversarial_HeaderDragMove_SafelyHandlesNonLeftMouseClick()
        {
            RunOnStaThread(dialog =>
            {
                var method = typeof(DiffViewerDialog).GetMethod("Header_MouseDown",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(method);

                // Simulating a Right-Click on header should not attempt DragMove and not throw
                var mouseArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                {
                    RoutedEvent = UIElement.MouseDownEvent
                };

                // Invoking Header_MouseDown with Right button
                method.Invoke(dialog, new object[] { dialog, mouseArgs });
            });
        }

        #endregion
    }
}
