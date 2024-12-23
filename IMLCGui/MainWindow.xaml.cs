﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;

namespace IMLCGui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private static string[] INJECT_DELAYS = {
            "00000",
            "00002",
            "00008",
            "00015",
            "00050",
            "00100",
            "00200",
            "00300",
            "00400",
            "00500",
            "00700",
            "01000",
            "01300",
            "01700",
            "02500",
            "03500",
            "05000",
            "09000",
            "20000",
        };

        // TODO: Refactor this so that there are not 2 variables to maintain
        private static readonly string MLC_DOWNLOAD_VERSION = "v3.11b";
        private static readonly string MLC_DOWNLOAD_URL = "https://downloadmirror.intel.com/834254/mlc_v3.11b.tgz";

        private Logger _logger;
        private CustomConfig _config;
        private MLCProcessManager _mlcProcessManager;
        private AutoUpdater autoUpdater;

        private bool runningTest = false;

        public MainWindow()
        {
            this._logger = new Logger("imlcgui.log");

            InitializeComponent();
            InitGUI();

            this.LoadConfiguration();
            this._mlcProcessManager = new MLCProcessManager(this._logger, this._config.Get("mlcPath", ""));
            this.ValidateMLC();
            this._mlcProcessManager.FetchVersion();
            this.TxtConfigurePath.Text = this._mlcProcessManager.Path;

            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                this.ShowMessageAsync("Warning", $"You are not running this application as an administrator.{Environment.NewLine}It is strongly recommended to run this program as an administrator to obtain accurate data.");
            }

            this.autoUpdater = new AutoUpdater();
            this.Title = $"{this.Title} v{this.autoUpdater.CurrentVersion}";
            if (this.ShouldCheckForUpdatesOnStart())
            {
                this.CheckForUpdates((success) =>
                {
                    BtnUpdate.IsEnabled = true;
                });
            }
            else
            {
                BtnUpdate.IsEnabled = true;
            }
        }

        private void InitGUI()
        {
            InitLatencyRows();
            InitLatencyInjectDelay();
        }

        private void InitLatencyRows()
        {
            for (int i = 0; i < INJECT_DELAYS.Length; i++)
            {
                LatencyRow rowLatency = new LatencyRow();
                rowLatency.ShowGridLines = true;
                rowLatency.InjectDelay = INJECT_DELAYS[i];

                RowDefinition rowDef = new RowDefinition();
                rowDef.Height = GridLength.Auto;
                this.GridLatency.RowDefinitions.Add(rowDef);

                Grid.SetRow(rowLatency, i + 1);
                Grid.SetColumn(rowLatency, 0);
                this.GridLatency.Children.Add(rowLatency);
            }
        }

        private void InitLatencyInjectDelay()
        {
            this.ComboBoxInjectDelay.Items.Clear();
            this.ComboBoxInjectDelay.Items.Add("All");
            foreach (var delay in INJECT_DELAYS)
            {
                this.ComboBoxInjectDelay.Items.Add(delay);
            }
        }

        private void LoadConfiguration()
        {
            this._config = new CustomConfig("imlcgui.properties");
        }

        private bool ShouldCheckForUpdatesOnStart()
        {
            if (this._config.Has("checkForUpdatesOnStart"))
            {
                return bool.Parse(this._config.Get("checkForUpdatesOnStart"));
            }
            else
            {
                this._config.Set("checkForUpdatesOnStart", "true");
                return true;
            }
        }

        private void CheckForUpdates(Action<bool> onComplete = null)
        {
            bool shouldCheckForUpdates = true;
            if (this._config.Has("lastUpdateCheck"))
            {
                long lastUpdateCheck = long.Parse(this._config.Get("lastUpdateCheck"));
                shouldCheckForUpdates = DateTimeOffset.UtcNow.Subtract(DateTimeOffset.FromUnixTimeMilliseconds(lastUpdateCheck)).TotalDays > 5;
            }
            this._config.Set("lastUpdateCheck", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            this._config.Save();
            if (!shouldCheckForUpdates)
            {
                if (onComplete != null) onComplete.Invoke(false);
                return;
            }
            Task.Run(async () =>
            {
                try
                {
                    this._logger.Log("Checking for updates...");
                    await autoUpdater.CheckForUpdates();
                    bool hasUpdate = autoUpdater.HasUpdateAvailable();
                    if (hasUpdate)
                    {
                        this._logger.Log($"Found new IMLCGui version: {AutoUpdater.FormatVersion(autoUpdater.GetLatestReleaseVersion())}");
                        this.Invoke(() =>
                        {
                            BtnUpdate.ToolTip = "Update available!";
                        });
                    }
                    else
                    {
                        this._logger.Log("No updates found. Running latest version: " + autoUpdater.CurrentVersion);
                    }
                    if (onComplete != null)
                    {
                        this.Invoke(() =>
                        {
                            onComplete.Invoke(true);
                        });
                    }
                }
                catch (Exception ex)
                {
                    this._logger.Error($"Failed to check for updates:", ex);
                    if (onComplete != null)
                    {
                        this.Invoke(() =>
                        {
                            onComplete.Invoke(false);
                        });
                    }
                }
            });
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                this._mlcProcessManager.Stop();
            }
            catch (Exception ex)
            {
                this._logger.Error("Failed to stop MLC on program exit:", ex);
            }
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            this.ShowMessageAsync("Information",
                $"Intel Memory Latency Checker GUI v{AutoUpdater.GetCurrentVersion()}" + Environment.NewLine +
                Environment.NewLine +
                "A GUI wrapper for Intel MLC made in C# by KingFaris10." + Environment.NewLine +
                Environment.NewLine +
                "https://github.com/FarisR99/IMLCGui" + Environment.NewLine +
                "https://kingfaris.co.uk/"
            );
        }

        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (this.autoUpdater.CheckingForUpdate) return;
            BtnUpdate.IsEnabled = false;
            if (!this.autoUpdater.HasUpdateAvailable())
            {
                this._config.Set("lastUpdateCheck", null);
                this.CheckForUpdates((successful) =>
                {
                    BtnUpdate.IsEnabled = true;
                    if (successful && !this.autoUpdater.HasUpdateAvailable())
                    {
                        this.ShowMessageAsync(
                            "Autoupdater",
                            $"No new update found.{Environment.NewLine}" +
                            $"Current version: {this.autoUpdater.CurrentVersion}"
                        );
                    }
                });
            }
            else
            {
                BtnUpdate.Invoke(async () =>
                {
                    try
                    {
                        MessageDialogResult updateDialogResult = await this.ShowMessageAsync(
                            "Autoupdater",
                            $"New update found! Click OK to download to current directory.{Environment.NewLine}" +
                            $"Current version: {this.autoUpdater.CurrentVersion}{Environment.NewLine}" +
                            $"New version: {AutoUpdater.FormatVersion(this.autoUpdater.GetLatestReleaseVersion())}",
                            MessageDialogStyle.AffirmativeAndNegative
                        );
                        if (updateDialogResult == MessageDialogResult.Affirmative)
                        {
                            try
                            {
                                await this.autoUpdater.DownloadLatest(this._logger);
                                BtnUpdate.Visibility = Visibility.Hidden;
                            }
                            catch (Exception ex)
                            {
                                this._logger.Error("Failed to update:", ex);
                            }
                        }
                        else
                        {
                            this._logger.Debug("Version update skipped.");
                        }
                    }
                    finally
                    {
                        this.Invoke(() =>
                        {
                            BtnUpdate.IsEnabled = true;
                        });
                    }
                });
            }
        }

        // MLC Handling

        private string ValidateMLC()
        {
            if (this._mlcProcessManager.Path.Trim().Length == 0)
            {
                this._mlcProcessManager.Path = FileUtils.GetCurrentPath("mlc.exe");
                if (File.Exists(this._mlcProcessManager.Path))
                {
                    try
                    {
                        this._logger.Log($"Found mlc.exe at: {this._mlcProcessManager.Path}");
                        this._config.Set("mlcPath", this._mlcProcessManager.Path);
                        this._config.Save();

                        this.TxtConfigurePath.Text = this._mlcProcessManager.Path;
                    }
                    catch (Exception ex)
                    {
                        this._logger.Error("Failed to save mlcPath to config:", ex);
                    }
                    this._mlcProcessManager.FetchVersion();
                    return null;
                }
            }
            if (!File.Exists(this._mlcProcessManager.Path))
            {
                return $"Failed to find MLC at \"{this._mlcProcessManager.Path}\". Please visit the Configure tab.";
            }
            return null;
        }

        private void HandleMLCButton(Action resetUI, string logMessage, Action taskAction)
        {
            if (this._mlcProcessManager.GetVersion() == null)
            {
                this.ShowMessageAsync("Error", "Could not fetch MLC version.");
                return;
            }
            if (this._mlcProcessManager.Stop())
            {
                if (this.runningTest)
                {
                    this.runningTest = false;
                    return;
                }
            }
            string validationResult = ValidateMLC();
            if (validationResult != null)
            {
                this.ShowMessageAsync("Error", validationResult);
                return;
            }

            this.runningTest = true;
            if (logMessage != null)
            {
                this._logger.Log(logMessage);
            }
            if (resetUI != null)
            {
                resetUI();
            }

            this._mlcProcessManager.CancellationTokenSource = new CancellationTokenSource();
            Task.Run(taskAction, this._mlcProcessManager.CancellationTokenSource.Token);
        }

        // Run Quick Run

        private void BtnQuickRun_Click(object sender, RoutedEventArgs e)
        {
            this.HandleMLCButton(() =>
            {
                this.TxtBoxQuickBandwidth.Text = "";
                this.TxtBoxQuickLatency.Text = "";
                this.BtnQuickRun.Content = "Cancel";
                this.ToggleOtherTabs(false);
            },
            "Running Intel MLC quick test",
            () =>
            {
                try
                {
                    if (!RunMLCQuickProcess(this._mlcProcessManager.CancellationTokenSource.Token, false, "bandwidth"))
                    {
                        this._mlcProcessManager.CancellationTokenSource = null;
                        return;
                    }
                    RunMLCQuickProcess(this._mlcProcessManager.CancellationTokenSource.Token, true, "latency");
                }
                catch (OperationCanceledException)
                {
                    this._mlcProcessManager.Kill();
                }
                catch (Exception ex)
                {
                    this._logger.Error("Failed to run Intel MLC quick test:", ex);
                }
                finally
                {
                    this.ResetQuickRunButton();
                }
            });
        }

        private void TxtBoxQuickBandwidth_DoubleClick(object sender, RoutedEventArgs e)
        {
            if (this._mlcProcessManager.ProcessId != -1)
            {
                return;
            }
            this.HandleMLCButton(() =>
            {
                this.TxtBoxQuickBandwidth.Text = "";
                this.BtnQuickRun.Content = "Cancel";
                this.ToggleOtherTabs(false);
            },
            "Running Intel MLC quick bandwidth test",
            () =>
            {
                try
                {
                    RunMLCQuickProcess(this._mlcProcessManager.CancellationTokenSource.Token, true, "bandwidth");
                }
                catch (OperationCanceledException)
                {
                    this._mlcProcessManager.Kill();
                }
                catch (Exception ex)
                {
                    this._logger.Error("Failed to run Intel MLC quick bandwidth test:", ex);
                }
                finally
                {
                    this.ResetQuickRunButton();
                }
            });
        }

        private void TxtBoxQuickLatency_DoubleClick(object sender, RoutedEventArgs e)
        {
            if (this._mlcProcessManager.ProcessId != -1)
            {
                return;
            }
            this.HandleMLCButton(() =>
            {
                this.TxtBoxQuickLatency.Text = "";
                this.BtnQuickRun.Content = "Cancel";
                this.ToggleOtherTabs(false);
            },
            "Running Intel MLC quick latency test",
            () =>
            {
                try
                {
                    RunMLCQuickProcess(this._mlcProcessManager.CancellationTokenSource.Token, true, "latency");
                }
                catch (OperationCanceledException)
                {
                    this._mlcProcessManager.Kill();
                }
                catch (Exception ex)
                {
                    this._logger.Error("Failed to run Intel MLC quick latency test:", ex);
                }
                finally
                {
                    this.ResetQuickRunButton();
                }
            });
        }

        private void ResetQuickRunButton()
        {
            this.runningTest = false;
            this.BtnQuickRun.Invoke(() =>
            {
                this.BtnQuickRun.Content = "Run";
                this.ToggleOtherTabs(true);
            });
        }

        private bool RunMLCQuickProcess(CancellationToken cancelToken, bool destroyCancelTokenSource, string mode)
        {
            string mlcArguments = null;
            if (mode == "bandwidth")
            {
                mlcArguments = MLCProcess.GenerateQuickBandwidthArguments(this._mlcProcessManager.GetVersion());
            }
            else if (mode == "latency")
            {
                mlcArguments = MLCProcess.GenerateQuickLatencyArguments(this._mlcProcessManager.GetVersion());
            }
            else
            {
                Console.Error.WriteLine($"Unknown run mode for quick test: {mode}");
                return true;
            }
            Process process = this._mlcProcessManager.StartProcess(mlcArguments);
            if (process == null)
            {
                return false;
            }
            cancelToken.ThrowIfCancellationRequested();

            this._mlcProcessManager.ConsumeOutput(process, cancelToken,
                (string line) => line.StartsWith("Numa node"),
                (string line) =>
                {
                    string[] lineSplit = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (lineSplit.Length == 2)
                    {
                        string performanceValue = lineSplit[1];
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (mode == "latency")
                                {
                                    this.TxtBoxQuickLatency.Text = performanceValue;
                                }
                                else if (mode == "bandwidth")
                                {
                                    this.TxtBoxQuickBandwidth.Text = performanceValue;
                                }
                            }
                            catch (Exception ex)
                            {
                                this._logger.Warn(
                                    $"thread={Thread.CurrentThread.Name}: Failed to update user interface:",
                                    $"thread={Thread.CurrentThread.Name}: {ex.Message}"
                                );
                            }
                        }));
                    }
                    else
                    {
                        this._logger.Warn("Found unknown output line");
                        this.ShowMessageAsync("Error", "Found unknown output line, please raise a new Issue on the GitHub repository with the most recent log file output.");
                    }
                    return false;
                }
            );

            cancelToken.ThrowIfCancellationRequested();
            this._mlcProcessManager.Kill(process, destroyCancelTokenSource);
            return true;
        }

        // Run Bandwidth

        private void BtnBandwidthRun_Click(object sender, RoutedEventArgs e)
        {
            bool peakInjection = this.ChckBoxBandwidthPeak.IsChecked ?? false;
            this.HandleMLCButton(() =>
            {
                this.TxtBoxBandwidthAll.Text = "";
                this.TxtBoxBandwidth31.Text = "";
                this.TxtBoxBandwidth21.Text = "";
                this.TxtBoxBandwidth11.Text = "";
                this.TxtBoxBandwidthStreamTriad.Text = "";
                this.BtnBandwidthRun.Content = "Cancel";
                this.ToggleOtherTabs(false);
            },
            "Running Intel MLC bandwidth test",
            () =>
            {
                try
                {
                    StartMLCBandwidth(this._mlcProcessManager.CancellationTokenSource.Token, peakInjection);
                }
                catch (OperationCanceledException)
                {
                    this._mlcProcessManager.Kill();
                }
                finally
                {
                    this.ResetBandwidthRunButton();
                }
            });
        }

        private void ResetBandwidthRunButton()
        {
            this.runningTest = false;
            this.BtnBandwidthRun.Invoke(() =>
            {
                this.BtnBandwidthRun.Content = "Run";
                this.ToggleOtherTabs(true);
            });
        }

        private bool StartMLCBandwidth(CancellationToken cancelToken, bool peakInjection)
        {
            Process process = this._mlcProcessManager.StartProcess(MLCProcess.GenerateBandwidthArguments(this._mlcProcessManager.GetVersion(), peakInjection));
            if (process == null)
            {
                return false;
            }
            cancelToken.ThrowIfCancellationRequested();

            int currRow = -1;
            this._mlcProcessManager.ConsumeOutput(process, cancelToken,
                (string line) =>
                {
                    if (line.StartsWith("Using traffic"))
                    {
                        currRow = 0;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                },
                (string line) =>
                {
                    string[] lineSplit = line.Split(new string[] { ":" }, StringSplitOptions.RemoveEmptyEntries);
                    if (lineSplit.Length > 1)
                    {
                        string bandwidth = TrimNumericString(lineSplit[lineSplit.Length - 1]);
                        double bandwidthDouble = -1D;
                        if (!double.TryParse(bandwidth, out bandwidthDouble))
                        {
                            this._logger.Warn(
                                $"Found unknown output line: \"{String.Join(":", lineSplit)}\"",
                                $"Could not parse \"{bandwidth}\""
                            );
                            this.ShowMessageAsync("Error", "Found unknown output line, please raise a new Issue on the GitHub repository with the most recent log file output.");
                            return false;
                        }
                        int finalCurrRow = currRow;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (finalCurrRow == 0)
                                {
                                    this.TxtBoxBandwidthAll.Text = bandwidth;
                                }
                                else if (finalCurrRow == 1)
                                {
                                    this.TxtBoxBandwidth31.Text = bandwidth;
                                }
                                else if (finalCurrRow == 2)
                                {
                                    this.TxtBoxBandwidth21.Text = bandwidth;
                                }
                                else if (finalCurrRow == 3)
                                {
                                    this.TxtBoxBandwidth11.Text = bandwidth;
                                }
                                else if (finalCurrRow == 4)
                                {
                                    this.TxtBoxBandwidthStreamTriad.Text = bandwidth;
                                }
                            }
                            catch (Exception ex)
                            {
                                this._logger.Warn(
                                    $"thread={Thread.CurrentThread.Name}: Failed to update user interface:",
                                    $"thread={Thread.CurrentThread.Name}: {ex.Message}"
                                );
                            }
                        }));
                    }
                    else
                    {
                        this._logger.Warn($"Found unknown output line: [{String.Join(", ", lineSplit)}]");
                        this.ShowMessageAsync("Error", "Found unknown output line, please raise a new Issue on the GitHub repository with the most recent log file output.");
                        return false;
                    }
                    currRow++;
                    if (currRow > 4)
                    {
                        return false;
                    }
                    return true;
                }
            );

            cancelToken.ThrowIfCancellationRequested();
            this._mlcProcessManager.Kill(process);
            if (currRow == -1)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    this.ShowMessageAsync("Error", "Failed to fetch bandwidth data from Intel MLC. Please check the logs file for any errors.");
                }));
                return true;
            }
            return true;
        }

        // Run Latency

        private void BtnLatencyRun_Click(object sender, RoutedEventArgs e)
        {
            string injectDelayOverride = (string)this.ComboBoxInjectDelay.SelectedItem;
            if (this.ComboBoxInjectDelay.SelectedIndex < 1)
            {
                injectDelayOverride = null;
            }

            this.HandleMLCButton(() =>
            {
                this.ProgressLatency.Value = 0;
                for (int i = 0; i < INJECT_DELAYS.Length; i++)
                {
                    LatencyRow rowLatency = (LatencyRow)this.GridLatency.Children[i + 1];
                    rowLatency.InjectDelay = INJECT_DELAYS[i];
                    rowLatency.Latency = "";
                    rowLatency.Bandwidth = "";
                }
                this.BtnLatencyRun.Content = "Cancel";
                this.ToggleOtherTabs(false);
            },
            "Running Intel MLC latency test",
            () =>
            {
                try
                {
                    StartMLCLatency(this._mlcProcessManager.CancellationTokenSource.Token, injectDelayOverride);
                }
                catch (OperationCanceledException)
                {
                    this._mlcProcessManager.Kill();
                }
                finally
                {
                    this.ResetLatencyRunButton();
                }
            });
        }

        private void ResetLatencyRunButton()
        {
            this.runningTest = false;
            this.BtnLatencyRun.Invoke(() =>
            {
                this.BtnLatencyRun.Content = "Run";
                this.ToggleOtherTabs(true);
            });
        }

        private bool StartMLCLatency(CancellationToken cancelToken, string injectDelayOverride)
        {
            Process process = this._mlcProcessManager.StartProcess(MLCProcess.GenerateLatencyArguments(this._mlcProcessManager.GetVersion(), injectDelayOverride));
            if (process == null)
            {
                return false;
            }
            cancelToken.ThrowIfCancellationRequested();

            int currRow = -1;
            this._mlcProcessManager.ConsumeOutput(process, cancelToken,
                (string line) =>
                {
                    if (line == "==========================")
                    {
                        currRow = 0;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                },
                (string line) =>
                {
                    string[] lineSplit = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (lineSplit.Length == 3)
                    {
                        string injectDelay = lineSplit[0];
                        int injectDelayIndex = Array.IndexOf(INJECT_DELAYS, injectDelay);
                        if (injectDelayIndex == -1)
                        {
                            this._logger.Warn($"thread={Thread.CurrentThread.Name}: Skipping unknown inject delay = {injectDelay}");
                            currRow++;
                            return true;
                        }
                        int progressValue = injectDelayOverride == null
                                                ? (int)((injectDelayIndex + 1) * 100 / (double)INJECT_DELAYS.Length)
                                                : 100;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                LatencyRow rowLatency = (LatencyRow)this.GridLatency.Children[injectDelayIndex + 1];
                                rowLatency.InjectDelay = lineSplit[0];
                                rowLatency.Latency = lineSplit[1];
                                rowLatency.Bandwidth = lineSplit[2];

                                this.ProgressLatency.Value = progressValue;

                                if (injectDelayIndex == 0)
                                {
                                    this.GridLatencyScroller.ScrollToTop();
                                }
                                else if (injectDelayIndex == INJECT_DELAYS.Length - 1)
                                {
                                    this.GridLatencyScroller.ScrollToBottom();
                                }
                                else
                                {
                                    double actualScrollHeight = this.GridLatencyScroller.ActualHeight;
                                    double actualRowHeight = rowLatency.ActualHeight;
                                    if (this.GridLatencyScroller.VerticalOffset + actualScrollHeight < actualRowHeight * (injectDelayIndex + 1))
                                    {
                                        this.GridLatencyScroller.ScrollToVerticalOffset(actualRowHeight * (injectDelayIndex + 2) - actualScrollHeight);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                this._logger.Warn(
                                    $"thread={Thread.CurrentThread.Name}: Failed to update user interface:",
                                    $"thread={Thread.CurrentThread.Name}: {ex.Message}"
                                );
                            }
                        }));
                    }
                    else
                    {
                        this._logger.Warn("Found unknown output line");
                        this.ShowMessageAsync("Error", "Found unknown output line, please raise a new Issue on the GitHub repository with the most recent log file output.");
                        return false;
                    }
                    currRow++;
                    if (currRow >= INJECT_DELAYS.Length)
                    {
                        return false;
                    }
                    return true;
                }
            );

            cancelToken.ThrowIfCancellationRequested();
            this._mlcProcessManager.Kill(process);
            if (currRow == -1)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    this.ShowMessageAsync("Error", "Failed to fetch latency data from Intel MLC. Please check the logs file for any errors.");
                }));
                return true;
            }
            return true;
        }

        // Run Cache

        private void BtnCacheRun_Click(object sender, RoutedEventArgs e)
        {
            this.HandleMLCButton(() =>
            {
                this.TxtBoxL2Hit.Text = "";
                this.TxtBoxL2HitM.Text = "";
                this.BtnCacheRun.Content = "Cancel";
                this.ToggleOtherTabs(false);
            },
            "Running Intel MLC cache test",
            () =>
            {
                try
                {
                    StartMLCCache(this._mlcProcessManager.CancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    this._mlcProcessManager.Kill();
                }
                finally
                {
                    this.ResetCacheRunButton();
                }
            });
        }

        private void ResetCacheRunButton()
        {
            this.runningTest = false;
            this.BtnCacheRun.Invoke(() =>
            {
                this.BtnCacheRun.Content = "Run";
                this.ToggleOtherTabs(true);
            });
        }

        private bool StartMLCCache(CancellationToken cancelToken)
        {
            Process process = this._mlcProcessManager.StartProcess(MLCProcess.GenerateCacheArguments(this._mlcProcessManager.GetVersion()));
            if (process == null)
            {
                return false;
            }
            cancelToken.ThrowIfCancellationRequested();

            int currRow = -1;
            this._mlcProcessManager.ConsumeOutput(process, cancelToken,
                (string line) =>
                {
                    if (line.StartsWith("Using small pages"))
                    {
                        currRow = 0;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                },
                (string line) =>
                {
                    string[] lineSplit = line.Split(new string[] { "latency" }, StringSplitOptions.RemoveEmptyEntries);
                    if (lineSplit.Length == 2)
                    {
                        string latency = TrimNumericString(lineSplit[1]);
                        int finalRow = currRow;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (finalRow == 0)
                                {
                                    this.TxtBoxL2Hit.Text = latency;
                                }
                                else if (finalRow == 1)
                                {
                                    this.TxtBoxL2HitM.Text = latency;
                                }
                            }
                            catch (Exception ex)
                            {
                                this._logger.Warn(
                                    $"thread={Thread.CurrentThread.Name}: Failed to update user interface:",
                                    $"thread={Thread.CurrentThread.Name}: {ex.Message}"
                                );
                            }
                        }));
                    }
                    else
                    {
                        this._logger.Warn($"Found unknown output line: [{String.Join(", ", lineSplit)}]");
                        this.ShowMessageAsync("Error", "Found unknown output line, please raise a new Issue on the GitHub repository with the most recent log file output.");
                        return false;
                    }
                    currRow++;
                    if (currRow > 1)
                    {
                        return false;
                    }
                    return true;
                }
            );

            cancelToken.ThrowIfCancellationRequested();
            this._mlcProcessManager.Kill(process);
            if (currRow == -1)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    this.ShowMessageAsync("Error", "Failed to fetch cache data from Intel MLC. Please check the logs file for any errors.");
                }));
                return true;
            }
            return true;
        }

        // Configure Window

        private object mlcDownloadLock = new object();
        private CancellationTokenSource mlcDownloadCancellationSource = null;

        private void BtnConfigureDownload_Click(object sender, RoutedEventArgs e)
        {
            lock (this.mlcDownloadLock)
            {
                string zipFileNameWithoutExt = $"mlc_{MLC_DOWNLOAD_VERSION}";
                string zipFileName = $"{zipFileNameWithoutExt}.tgz";
                string mlcUrl = MLC_DOWNLOAD_URL;
                string tmpZipDestination = FileUtils.GetTempPath(zipFileName);

                if (this.mlcDownloadCancellationSource != null)
                {
                    this.mlcDownloadCancellationSource.Cancel();
                    this.mlcDownloadCancellationSource = null;
                    try
                    {
                        Thread.Sleep(250);
                        if (File.Exists(tmpZipDestination))
                        {
                            File.Delete(tmpZipDestination);
                        }
                    }
                    catch
                    {
                    }

                    lock (this.mlcDownloadProgressLock)
                    {
                        this.mlcDownloadLastProgress = -1;
                    }
                    return;
                }
                lock (this._mlcProcessManager.ProcessLock)
                {
                    if (this._mlcProcessManager.ProcessId != -1)
                    {
                        this.ShowMessageAsync("Error", "Cannot modify MLC path whilst MLC is running.");
                        return;
                    }
                }

                this.TxtConfigureLog.ScrollToHome();
                this.TxtConfigureLog.Text = "";
                this._logger.Log("Downloading and extracting MLC...");
                this.WriteToConfigureLog(
                    $"Downloading Intel MLC...",
                    $"URL: {mlcUrl}",
                    $"Temporary destination: {tmpZipDestination}"
                );

                if (File.Exists(tmpZipDestination))
                {
                    this._logger.Log($"Deleting existing file at \"{tmpZipDestination}\"");
                    try
                    {
                        File.Delete(tmpZipDestination);
                    }
                    catch (Exception ex)
                    {
                        this._logger.Error($"Failed to delete existing file at \"{tmpZipDestination}\":", ex);
                        return;
                    }
                    this.WriteToConfigureLog("Deleted existing MLC file.");
                }

                this.BtnConfigureDownload.Content = "Cancel";

                this.mlcDownloadCancellationSource = new CancellationTokenSource();
                CancellationToken cancelToken = this.mlcDownloadCancellationSource.Token;
                Task.Run(() =>
                {
                    DownloadMLC(cancelToken, mlcUrl, tmpZipDestination, () =>
                    {
                        try
                        {
                            lock (this.mlcDownloadProgressLock)
                            {
                                this.mlcDownloadLastProgress = -1;
                            }
                            if (!FileUtils.DoesExist(tmpZipDestination))
                            {
                                this._logger.Warn($"Failed to locate downloaded file at \"{tmpZipDestination}\"");
                                Dispatcher.Invoke(() =>
                                {
                                    this.WriteToConfigureLog($"Failed to locate downloaded file at: {tmpZipDestination}");
                                });
                                return;
                            }
                            else
                            {
                                Dispatcher.Invoke(() => this.WriteToConfigureLog("Downloaded!"));
                            }
                            string extractedZipDirectory = FileUtils.GetTempPath("mlc");
                            if (FileUtils.DoesExist(extractedZipDirectory))
                            {
                                this._logger.Log($"Deleting existing temporary MLC directory at \"{extractedZipDirectory}\"");
                                FileUtils.Delete(extractedZipDirectory);
                            }
                            this._logger.Log($"File moved, extracting to \"{extractedZipDirectory}\"");
                            Dispatcher.Invoke(() =>
                            {
                                this.WriteToConfigureLog($"File moved, extracting to: {extractedZipDirectory}");
                            });
                            cancelToken = this.mlcDownloadCancellationSource.Token;
                            ExtractMLC(cancelToken, tmpZipDestination, extractedZipDirectory, () =>
                            {
                                try
                                {
                                    this._logger.Log($"Deleting \"{tmpZipDestination}\"...");
                                    FileUtils.Delete(tmpZipDestination);
                                    string mlcWindowsPath = Path.Combine(Path.Combine(extractedZipDirectory, zipFileNameWithoutExt), "Windows");
                                    string finalMLCPath = FileUtils.GetCurrentPath("mlc");
                                    if (!Directory.Exists(mlcWindowsPath))
                                    {
                                        mlcWindowsPath = Path.Combine(extractedZipDirectory, "Windows");
                                    }
                                    if (!Directory.Exists(mlcWindowsPath))
                                    {
                                        this._logger.Warn($"Failed to locate extracted directory at \"{mlcWindowsPath}\"");
                                        FileUtils.Delete(extractedZipDirectory);
                                        Dispatcher.Invoke(() =>
                                        {
                                            this.WriteToConfigureLog($"Failed to locate MLC at: {mlcWindowsPath}");
                                        });
                                        return;
                                    }
                                    if (FileUtils.DoesExist(finalMLCPath))
                                    {
                                        this._logger.Log($"Deleting existing MLC directory at \"{finalMLCPath}\"");
                                        FileUtils.Delete(finalMLCPath);
                                    }
                                    this._logger.Log($"Moving MLC from \"{mlcWindowsPath}\" to \"{finalMLCPath}\"");
                                    FileUtils.CopyAndMove(mlcWindowsPath, finalMLCPath);
                                    FileUtils.Delete(mlcWindowsPath);
                                    this._logger.Log($"Moved MLC to \"{finalMLCPath}\"");
                                    if (FileUtils.DoesExist(extractedZipDirectory))
                                    {
                                        this._logger.Log($"Deleting temporary MLC directory at \"{extractedZipDirectory}");
                                        FileUtils.Delete(extractedZipDirectory);
                                    }
                                    Dispatcher.Invoke(() =>
                                    {
                                        this.WriteToConfigureLog($"Extracted MLC to: {finalMLCPath}");
                                        this.WriteToConfigureLog("Success!");

                                        this._mlcProcessManager.Path = Path.Combine(finalMLCPath, "mlc.exe");
                                        this._mlcProcessManager.FetchVersion();
                                        this.TxtConfigurePath.Text = this._mlcProcessManager.Path;
                                        try
                                        {
                                            this._config.Set("mlcPath", this._mlcProcessManager.Path);
                                            this._config.Save();
                                        }
                                        catch (Exception ex)
                                        {
                                            this._logger.Error("Failed to update mlcPath in config:", ex);
                                        }
                                    });
                                }
                                catch (Exception ex)
                                {
                                    this._logger.Error($"Failed to extract {tmpZipDestination}:", ex);
                                }
                                finally
                                {
                                    lock (this.mlcDownloadLock)
                                    {
                                        this.mlcDownloadCancellationSource = null;
                                    }
                                }
                            }, null, () =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    this.WriteToConfigureLog("", "Failed to extract MLC.", "Please check the logs for more information.");
                                });
                                lock (this.mlcDownloadLock)
                                {
                                    this.mlcDownloadCancellationSource = null;
                                }
                            }, () =>
                            {
                                Dispatcher.Invoke(() => this.BtnConfigureDownload.Content = "Download");
                            });
                        }
                        catch (Exception ex)
                        {
                            this._logger.Error($"Failed to download MLC to {tmpZipDestination}:", ex);
                        }
                    }, () =>
                    {
                        Dispatcher.Invoke(() => this.BtnConfigureDownload.Content = "Download");
                    }, () =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            this.WriteToConfigureLog("", "Failed to download MLC.", "Please check the logs for more information.");
                            Dispatcher.Invoke(() => this.BtnConfigureDownload.Content = "Download");
                        });
                        lock (this.mlcDownloadLock)
                        {
                            lock (this.mlcDownloadProgressLock)
                            {
                                this.mlcDownloadLastProgress = -1;
                            }
                            this.mlcDownloadCancellationSource = null;
                        }
                    });
                }, cancelToken);
            }
        }

        private async void DownloadMLC(CancellationToken cancelToken, string mlcUrl, string zipDestination, Action onComplete, Action onCancel, Action onFail)
        {
            Task<string> task = DownloadService.DownloadFileAsync(cancelToken, mlcUrl, zipDestination, MLCWebClient_DownloadProgressChanged);
            try
            {
                await task;
                if (onComplete != null) onComplete();
            }
            catch (OperationCanceledException)
            {
                if (onCancel != null) onCancel();
            }
            catch (Exception ex)
            {
                this._logger.Error("Failed to download MLC:", ex);
                if (onFail != null) onFail();
            }
        }

        private async void ExtractMLC(CancellationToken cancelToken, string zipFile, string destination, Action onComplete, Action onCancel, Action onFail, Action finalRunnable)
        {
            Task task = DownloadService.ExtractTGZ(cancelToken, zipFile, destination);
            try
            {
                await task;
                if (onComplete != null) onComplete();
            }
            catch (OperationCanceledException)
            {
                if (onCancel != null) onCancel();
            }
            catch (Exception ex)
            {
                this._logger.Error("Failed to extract MLC:", ex);
                if (onFail != null) onFail();
            }
            finally
            {
                if (finalRunnable != null) finalRunnable();
            }
        }

        private object mlcDownloadProgressLock = new object();
        private int mlcDownloadLastProgress = -1;

        private void MLCWebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            int progressPercentage = e.ProgressPercentage;
            lock (mlcDownloadProgressLock)
            {
                if (this.mlcDownloadLastProgress != progressPercentage)
                {
                    this.mlcDownloadLastProgress = progressPercentage;
                    Dispatcher.Invoke(() => this.WriteToConfigureLog($"Downloading: {this.mlcDownloadLastProgress}%"));
                }
            }
        }

        private void WriteToConfigureLog(params string[] logMessages)
        {
            foreach (var logMessage in logMessages)
            {
                if (logMessage.Length > 0)
                {
                    this.TxtConfigureLog.Text += $"[{DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()}]: {logMessage}" + Environment.NewLine;
                }
                else
                {
                    this.TxtConfigureLog.Text += Environment.NewLine;
                }
            }
            if (!this.TxtConfigureLog.IsFocused)
            {
                this.TxtConfigureLog.ScrollToEnd();
            }
        }

        private void BtnConfigureBrowse_Click(object sender, RoutedEventArgs e)
        {
            lock (this._mlcProcessManager.ProcessLock)
            {
                if (this._mlcProcessManager.ProcessId != -1)
                {
                    this.ShowMessageAsync("Error", "Cannot modify MLC path whilst MLC is running.");
                    return;
                }
            }
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.CheckFileExists = true;
            openFileDialog.DefaultExt = ".exe";
            openFileDialog.AddExtension = true;
            openFileDialog.Filter = "Executable files (*.exe)|*.exe|MLC executable|mlc.exe|All files (*.*)|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                if (!File.Exists(openFileDialog.FileName))
                {
                    this.ShowMessageAsync("Error", "Please select a valid exe.");
                    return;
                }
                this._mlcProcessManager.Path = openFileDialog.FileName;
                this.TxtConfigurePath.Text = this._mlcProcessManager.Path;
                try
                {
                    this._config.Set("mlcPath", openFileDialog.FileName);
                    this._config.Save();
                    this._logger.Log($"Updated mlcPath to: {this._mlcProcessManager.Path}");
                }
                catch (Exception ex)
                {
                    this._logger.Error("Failed to save mlcPath to config:", ex);
                }
            }
        }

        private void TxtConfigurePath_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                lock (this._mlcProcessManager.ProcessLock)
                {
                    if (this._mlcProcessManager.ProcessId != -1)
                    {
                        this.ShowMessageAsync("Error", "Cannot modify MLC path whilst MLC is running.");
                        return;
                    }
                }
                this._mlcProcessManager.Path = this.TxtConfigurePath.Text.Trim();
                try
                {
                    this._config.Set("mlcPath", this._mlcProcessManager.Path);
                    this._config.Save();
                    this._logger.Log($"Updated mlcPath manually to: {this._mlcProcessManager.Path}");
                }
                catch (Exception ex)
                {
                    this._logger.Error("Failed to save mlcPath to config:", ex);
                }
            }
        }

        private void ToggleOtherTabs(bool enabled)
        {
            for (int tabIndex = 0; tabIndex < this.TabCtrlBenchmark.Items.Count; tabIndex++)
            {
                if (tabIndex != this.TabCtrlBenchmark.SelectedIndex)
                {
                    MetroTabItem tab = (MetroTabItem)this.TabCtrlBenchmark.Items[tabIndex];
                    tab.IsEnabled = enabled;
                }
            }
        }

        private static string TrimNumericString(string inputString)
        {
            inputString = inputString.Trim();
            string newString = "";
            for (int i = 0; i < inputString.Length; i++)
            {
                char characterAt = inputString[i];
                if ((characterAt >= '0' && characterAt <= '9') || characterAt == '.' || characterAt == ',')
                {
                    newString += characterAt;
                }
                else
                {
                    if (newString.Length > 0)
                    {
                        break;
                    }
                }
            }
            return newString;
        }
    }
}
