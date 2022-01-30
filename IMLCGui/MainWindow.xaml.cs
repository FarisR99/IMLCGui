using System;
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

        private Logger _logger;
        private CustomConfig _config;
        private MLCProcess _mlcProcess;

        public MainWindow()
        {
            this._logger = new Logger("imlcgui.log");

            InitializeComponent();
            FillLatencyRows();

            this.LoadConfiguration();
            this._mlcProcess = new MLCProcess(this._logger, this._config.Get("mlcPath", ""));
            this.TxtConfigurePath.Text = this._mlcProcess.Path;

            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                this.ShowMessageAsync("Warning", $"You are not running this application as an administrator.{Environment.NewLine}It is strongly recommended to run this program as an administrator to obtain accurate data.");
            }
        }

        private void FillLatencyRows()
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

        private void LoadConfiguration()
        {
            this._config = new CustomConfig("imlcgui.properties");
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                this._mlcProcess.Stop();
            }
            catch (Exception ex)
            {
                this._logger.Error("Failed to stop MLC on program exit:", ex);
            }
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            this.ShowMessageAsync("Information",
                "Intel Memory Latency Checker GUI" + Environment.NewLine +
                Environment.NewLine +
                "A GUI wrapper for Intel MLC made in C# by KingFaris10."
            );
        }

        // MLC Handling

        private string ValidateMLC()
        {
            if (this._mlcProcess.Path.Trim().Length == 0)
            {
                this._mlcProcess.Path = FileUtils.GetCurrentPath("mlc.exe");
                if (File.Exists(this._mlcProcess.Path))
                {
                    try
                    {
                        this._logger.Log($"Found mlc.exe at: {this._mlcProcess.Path}");
                        this._config.Set("mlcPath", this._mlcProcess.Path);
                        this._config.Save();

                        this.TxtConfigurePath.Text = this._mlcProcess.Path;
                    }
                    catch (Exception ex)
                    {
                        this._logger.Error("Failed to save mlcPath to config:", ex);
                    }
                    return null;
                }
            }
            if (!File.Exists(this._mlcProcess.Path))
            {
                return $"Failed to find MLC at \"{this._mlcProcess.Path}\". Please visit the Configure tab.";
            }
            return null;
        }

        private void HandleMLCButton(Action resetUI, string logMessage, Action taskAction)
        {
            if (this._mlcProcess.Stop())
            {
                return;
            }
            string validationResult = ValidateMLC();
            if (validationResult != null)
            {
                this.ShowMessageAsync("Error", validationResult);
                return;
            }

            if (resetUI != null)
            {
                resetUI();
            }

            if (logMessage != null)
            {
                this._logger.Log(logMessage);
            }

            this._mlcProcess.CancellationTokenSource = new CancellationTokenSource();
            Task.Run(taskAction, this._mlcProcess.CancellationTokenSource.Token);
        }

        // Run Quick Run

        private void BtnQuickRun_Click(object sender, RoutedEventArgs e)
        {
            this.HandleMLCButton(() =>
            {
                this.TxtBoxQuickBandwidth.Text = "";
                this.TxtBoxQuickLatency.Text = "";
                this.BtnQuickRun.Content = "Cancel";
            },
            "Running Intel MLC quick test",
            () =>
            {
                try
                {
                    if (!RunMLCQuickProcess(this._mlcProcess.CancellationTokenSource.Token, "bandwidth"))
                    {
                        return;
                    }
                    RunMLCQuickProcess(this._mlcProcess.CancellationTokenSource.Token, "latency");
                }
                catch (OperationCanceledException)
                {
                    this._mlcProcess.Kill();
                }
                finally
                {
                    this.ResetQuickRunButton();
                }
            });
        }

        private void ResetQuickRunButton()
        {
            this.BtnQuickRun.Invoke(() =>
            {
                this.BtnQuickRun.Content = "Run";
            });
        }

        private bool RunMLCQuickProcess(CancellationToken cancelToken, string mode)
        {
            string mlcArguments = "";
            if (mode == "bandwidth")
            {
                mlcArguments = "--bandwidth_matrix";
            }
            else if (mode == "latency")
            {
                mlcArguments = "--latency_matrix";
            }
            else
            {
                Console.Error.WriteLine($"Unknown run mode for quick test: {mode}");
                return true;
            }
            Process process = this._mlcProcess.StartProcess(mlcArguments);
            if (process == null)
            {
                return false;
            }
            cancelToken.ThrowIfCancellationRequested();

            this._mlcProcess.ConsumeOutput(process, cancelToken,
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
                                this._logger.Log(LogLevel.WARN,
                                    $"thread={Thread.CurrentThread.Name}: Failed to update user interface:",
                                    $"thread={Thread.CurrentThread.Name}: {ex.Message}"
                                );
                            }
                        }));
                    }
                    else
                    {
                        this._logger.Log(LogLevel.WARN, "Found unknown output line");
                    }
                    return false;
                }
            );

            cancelToken.ThrowIfCancellationRequested();
            this._mlcProcess.Kill(process);
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
            },
            "Running Intel MLC bandwidth test",
            () =>
            {
                try
                {
                    StartMLCBandwidth(this._mlcProcess.CancellationTokenSource.Token, peakInjection);
                }
                catch (OperationCanceledException)
                {
                    this._mlcProcess.Kill();
                }
                finally
                {
                    this.ResetBandwidthRunButton();
                }
            });
        }

        private void ResetBandwidthRunButton()
        {
            this.BtnBandwidthRun.Invoke(() =>
            {
                this.BtnBandwidthRun.Content = "Run";
            });
        }

        private bool StartMLCBandwidth(CancellationToken cancelToken, bool peakInjection)
        {
            string mlcArguments = peakInjection ? "--peak_injection_bandwidth" : "--max_bandwidth";
            Process process = this._mlcProcess.StartProcess(mlcArguments);
            if (process == null)
            {
                return false;
            }
            cancelToken.ThrowIfCancellationRequested();

            int currRow = -1;
            this._mlcProcess.ConsumeOutput(process, cancelToken,
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
                        string bandwidth = lineSplit[lineSplit.Length - 1].Trim();
                        double bandwidthDouble = -1D;
                        if (!double.TryParse(bandwidth, out bandwidthDouble))
                        {
                            this._logger.Log(LogLevel.WARN, $"Found unknown output line: [{String.Join(", ", lineSplit)}]");
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
                                this._logger.Log(LogLevel.WARN,
                                    $"thread={Thread.CurrentThread.Name}: Failed to update user interface:",
                                    $"thread={Thread.CurrentThread.Name}: {ex.Message}"
                                );
                            }
                        }));
                    }
                    else
                    {
                        this._logger.Log(LogLevel.WARN, $"Found unknown output line: [{String.Join(", ", lineSplit)}]");
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
            this._mlcProcess.Kill(process);
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
            },
            "Running Intel MLC latency test",
            () =>
            {
                try
                {
                    StartMLCLatency(this._mlcProcess.CancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    this._mlcProcess.Kill();
                }
                finally
                {
                    this.ResetLatencyRunButton();
                }
            });
        }

        private void ResetLatencyRunButton()
        {
            this.BtnLatencyRun.Invoke(() =>
            {
                this.BtnLatencyRun.Content = "Run";
            });
        }

        private bool StartMLCLatency(CancellationToken cancelToken)
        {
            Process process = this._mlcProcess.StartProcess("--loaded_latency");
            if (process == null)
            {
                return false;
            }
            cancelToken.ThrowIfCancellationRequested();

            int currRow = -1;
            this._mlcProcess.ConsumeOutput(process, cancelToken,
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
                            this._logger.Log(LogLevel.WARN, $"thread={Thread.CurrentThread.Name}: Skipping unknown inject delay = {injectDelay}");
                            currRow++;
                            return true;
                        }
                        int progressValue = (int)((injectDelayIndex + 1) * 100 / (double)INJECT_DELAYS.Length);
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                LatencyRow rowLatency = (LatencyRow)this.GridLatency.Children[injectDelayIndex + 1];
                                rowLatency.InjectDelay = lineSplit[0];
                                rowLatency.Latency = lineSplit[1];
                                rowLatency.Bandwidth = lineSplit[2];
                                this.ProgressLatency.Value = progressValue;
                            }
                            catch (Exception ex)
                            {
                                this._logger.Log(LogLevel.WARN,
                                    $"thread={Thread.CurrentThread.Name}: Failed to update user interface:",
                                    $"thread={Thread.CurrentThread.Name}: {ex.Message}"
                                );
                            }
                        }));
                    }
                    else
                    {
                        this._logger.Log(LogLevel.WARN, "Found unknown output line");
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
            this._mlcProcess.Kill(process);
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
            },
            "Running Intel MLC cache test",
            () =>
            {
                try
                {
                    StartMLCCache(this._mlcProcess.CancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    this._mlcProcess.Kill();
                }
                finally
                {
                    this.ResetCacheRunButton();
                }
            });
        }

        private void ResetCacheRunButton()
        {
            this.BtnCacheRun.Invoke(() =>
            {
                this.BtnCacheRun.Content = "Run";
            });
        }

        private bool StartMLCCache(CancellationToken cancelToken)
        {
            Process process = this._mlcProcess.StartProcess("--c2c_latency");
            if (process == null)
            {
                return false;
            }
            cancelToken.ThrowIfCancellationRequested();

            int currRow = -1;
            this._mlcProcess.ConsumeOutput(process, cancelToken,
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
                        string latency = lineSplit[1].Trim();
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
                                this._logger.Log(LogLevel.WARN,
                                    $"thread={Thread.CurrentThread.Name}: Failed to update user interface:",
                                    $"thread={Thread.CurrentThread.Name}: {ex.Message}"
                                );
                            }
                        }));
                    }
                    else
                    {
                        this._logger.Log(LogLevel.WARN, $"Found unknown output line: [{String.Join(", ", lineSplit)}]");
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
            this._mlcProcess.Kill(process);
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
                string zipFileName = "mlc_v3.9a.tgz";
                string mlcUrl = $"https://www.intel.com/content/dam/develop/external/us/en/documents/{zipFileName}";
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
                lock (this._mlcProcess.ProcessLock)
                {
                    if (this._mlcProcess.ProcessId != -1)
                    {
                        this.ShowMessageAsync("Cannot modify MLC path whilst MLC is running.", "Error");
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
                                this._logger.Log($"Failed to locate downloaded file at \"{tmpZipDestination}\"");
                                Dispatcher.Invoke(() =>
                                {
                                    this.WriteToConfigureLog($"Failed to locate downloaded file at: {tmpZipDestination}");
                                });
                                return;
                            }
                            else
                            {
                                Dispatcher.Invoke(() => this.WriteToConfigureLog("Downloaded!", ""));
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
                                    string mlcWindowsPath = Path.Combine(extractedZipDirectory, "Windows");
                                    string finalMLCPath = FileUtils.GetCurrentPath("mlc");
                                    if (!Directory.Exists(mlcWindowsPath))
                                    {
                                        this._logger.Log($"Failed to locate extracted directory at \"{mlcWindowsPath}\"");
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
                                    Dispatcher.Invoke(() =>
                                    {
                                        this.WriteToConfigureLog($"Extracted MLC to: {finalMLCPath}");
                                        this.WriteToConfigureLog("Success!");

                                        this._mlcProcess.Path = Path.Combine(finalMLCPath, "mlc.exe");
                                        this.TxtConfigurePath.Text = this._mlcProcess.Path;
                                        try
                                        {
                                            this._config.Set("mlcPath", this._mlcProcess.Path);
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
            lock (this._mlcProcess.ProcessLock)
            {
                if (this._mlcProcess.ProcessId != -1)
                {
                    this.ShowMessageAsync("Error", "Cannot modify MLC path whilst MLC is running.");
                    return;
                }
            }
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                if (!openFileDialog.CheckFileExists)
                {
                    this.ShowMessageAsync("Error", "Please select a valid exe.");
                    return;
                }
                this._mlcProcess.Path = openFileDialog.FileName;
                this.TxtConfigurePath.Text = this._mlcProcess.Path;
                try
                {
                    this._config.Set("mlcPath", openFileDialog.FileName);
                    this._config.Save();
                    this._logger.Log($"Updated mlcPath to: {this._mlcProcess.Path}");
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
                lock (this._mlcProcess.ProcessLock)
                {
                    if (this._mlcProcess.ProcessId != -1)
                    {
                        this.ShowMessageAsync("Error", "Cannot modify MLC path whilst MLC is running.");
                        return;
                    }
                }
                this._mlcProcess.Path = this.TxtConfigurePath.Text.Trim();
                try
                {
                    this._config.Set("mlcPath", this._mlcProcess.Path);
                    this._config.Save();
                    this._logger.Log($"Updated mlcPath manually to: {this._mlcProcess.Path}");
                }
                catch (Exception ex)
                {
                    this._logger.Error("Failed to save mlcPath to config:", ex);
                }
            }
        }
    }
}
