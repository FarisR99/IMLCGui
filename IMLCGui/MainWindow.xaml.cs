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

        private Logger logger;
        private CustomConfig config;

        private string mlcPath = "";

        private CancellationTokenSource mlcCancellationTokenSource;
        private object mlcProcessLock = new object();
        private int mlcProcessId = -1;

        public MainWindow()
        {
            this.logger = new Logger("log.txt");

            InitializeComponent();

            for (int i = 0; i < INJECT_DELAYS.Length; i++)
            {
                LatencyRow RowLatency = new LatencyRow();
                RowLatency.ShowGridLines = true;
                RowLatency.InjectDelay = INJECT_DELAYS[i];

                RowDefinition RowDef = new RowDefinition();
                RowDef.Height = GridLength.Auto;
                this.GridLatency.RowDefinitions.Add(RowDef);

                Grid.SetRow(RowLatency, i + 1);
                Grid.SetColumn(RowLatency, 0);
                this.GridLatency.Children.Add(RowLatency);
            }

            this.LoadConfiguration();

            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                MessageBox.Show(
                    $"WARNING: You are not running this application as an administrator.{Environment.NewLine}It is strongly recommended to run this program as an administrator to obtain accurate data.",
                    "Warning"
                );
            }
        }

        private void LoadConfiguration()
        {
            this.config = new CustomConfig("imlcgui.properties");
            this.mlcPath = this.config.Get("mlcPath", "");
            this.TxtConfigurePath.Text = this.mlcPath;
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                StopMLC();
            }
            catch (Exception ex)
            {
                this.logger.Error("Failed to stop MLC on program exit:", ex);
            }
            try
            {
                if (this.mlcCancellationTokenSource != null)
                {
                    this.mlcDownloadCancellationSource.Cancel();
                }
            }
            catch
            {
            }
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Intel Memory Latency Checker GUI" + Environment.NewLine +
                Environment.NewLine +
                "A GUI wrapper for Intel MLC made in C# by KingFaris10.",
                "Help"
            );
        }

        // Core

        public delegate void Runnable();

        // MLC Handling

        private string ValidateMLC()
        {
            if (this.mlcPath.Trim().Length == 0)
            {
                this.mlcPath = FileUtils.GetCurrentPath("mlc.exe");
                if (File.Exists(this.mlcPath))
                {
                    try
                    {
                        this.logger.Log($"Found mlc.exe at: {this.mlcPath}");
                        this.config.Set("mlcPath", this.mlcPath);
                        this.config.Save();
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error("Failed to save mlcPath to config:", ex);
                    }
                    return null;
                }
            }
            if (!File.Exists(this.mlcPath))
            {
                return $"Failed to find MLC at \"{this.mlcPath}\". Please visit the Configure tab.";
            }
            return null;
        }

        private bool StopMLC()
        {
            bool retVal = false;
            if (this.mlcCancellationTokenSource != null && !this.mlcCancellationTokenSource.IsCancellationRequested)
            {
                this.mlcCancellationTokenSource.Cancel();
                this.mlcCancellationTokenSource = null;
                retVal = true;
                try
                {
                    Thread.Sleep(250);
                }
                catch (Exception)
                {
                }
            }
            lock (this.mlcProcessLock)
            {
                if (this.mlcProcessId != -1)
                {
                    try
                    {
                        this.logger.Log("User requested cancellation, killing MLC...");
                        Process runningProcess = Process.GetProcessById(this.mlcProcessId);
                        if (runningProcess != null && !runningProcess.HasExited)
                        {
                            if (!runningProcess.HasExited)
                            {
                                string processName = runningProcess.ProcessName;
                                runningProcess.Kill();
                                runningProcess.WaitForExit();
                                this.logger.Log($"Killed process: {processName}");
                                this.mlcProcessId = -1;
                            }
                        }
                        else
                        {
                            this.logger.Log("Killed MLC.");
                            this.mlcProcessId = -1;
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error("Failed to kill MLC process:", ex);
                    }
                    retVal = true;
                }
            }
            return retVal;
        }

        private void KillMLC(Process process)
        {
            lock (this.mlcProcessLock)
            {
                if (process == null)
                {
                    if (this.mlcProcessId == -1)
                    {
                        return;
                    }
                    process = Process.GetProcessById(this.mlcProcessId);
                }
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error($"Failed to kill {process.ProcessName}", ex);
                    }
                }
                this.mlcProcessId = -1;
            }
        }

        private delegate bool ProcessMLCOutputLine(string line);

        private void ConsumeMLCOutput(Process process, CancellationToken cancelToken, Predicate<string> shouldStartProcessing, ProcessMLCOutputLine processLine)
        {
            bool shouldProcessLine = false;
            while (!process.StandardOutput.EndOfStream)
            {
                cancelToken.ThrowIfCancellationRequested();
                string line;
                try
                {
                    line = process.StandardOutput.ReadLine();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return;
                }
                cancelToken.ThrowIfCancellationRequested();
                this.logger.Log(LogLevel.PROCESS, line);
                if (shouldProcessLine)
                {
                    if (!processLine(line))
                    {
                        break;
                    }
                }
                else if (shouldStartProcessing(line))
                {
                    shouldProcessLine = true;
                }
            }
            return;
        }

        // Run Quick Run

        private void BtnQuickRun_Click(object sender, RoutedEventArgs e)
        {
            if (StopMLC())
            {
                return;
            }
            string validationResult = ValidateMLC();
            if (validationResult != null)
            {
                MessageBox.Show(validationResult, "Error");
                return;
            }

            this.TxtBoxQuickBandwidth.Text = "";
            this.TxtBoxQuickLatency.Text = "";
            this.BtnQuickRun.Content = "Cancel";

            this.logger.Log("Running Intel MLC quick test");

            this.mlcCancellationTokenSource = new CancellationTokenSource();
            Task.Run(() =>
            {
                try
                {
                    if (!RunMLCQuickProcess(this.mlcCancellationTokenSource.Token, "bandwidth"))
                    {
                        return;
                    }
                    RunMLCQuickProcess(this.mlcCancellationTokenSource.Token, "latency");
                }
                catch (OperationCanceledException)
                {
                    this.KillMLC(null);
                }
                finally
                {
                    this.ResetQuickRunButton();
                }
            }, this.mlcCancellationTokenSource.Token);
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
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = this.mlcPath,
                    Arguments = mlcArguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true
            };
            lock (this.mlcProcessLock)
            {
                if (this.mlcProcessId != -1)
                {
                    return false;
                }
                this.logger.Log($"Running \"mlc {mlcArguments}\"");
                process.Start();
                this.mlcProcessId = process.Id;
            }
            cancelToken.ThrowIfCancellationRequested();

            ConsumeMLCOutput(process, cancelToken,
                delegate (string line) { return line.StartsWith("Numa node"); },
                delegate (string line)
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
                                this.logger.Log(LogLevel.WARN,
                                    $"thread={Thread.CurrentThread.Name}: Failed to update user interface:",
                                    $"thread={Thread.CurrentThread.Name}: {ex.Message}"
                                );
                            }
                        }));
                    }
                    else
                    {
                        this.logger.Log(LogLevel.WARN, "Found unknown output line");
                    }
                    return false;
                }
            );

            cancelToken.ThrowIfCancellationRequested();
            this.KillMLC(process);
            return true;
        }

        // Run Bandwidth

        private void BtnBandwidthRun_Click(object sender, RoutedEventArgs e)
        {
            if (StopMLC())
            {
                return;
            }
            string validationResult = ValidateMLC();
            if (validationResult != null)
            {
                MessageBox.Show(validationResult, "Error");
                return;
            }

            this.TxtBoxBandwidthAll.Text = "";
            this.TxtBoxBandwidth31.Text = "";
            this.TxtBoxBandwidth21.Text = "";
            this.TxtBoxBandwidth11.Text = "";
            this.TxtBoxBandwidthStreamTriad.Text = "";
            this.BtnBandwidthRun.Content = "Cancel";
            bool peakInjection = this.ChckBoxBandwidthPeak.IsChecked ?? false;

            this.logger.Log("Running Intel MLC bandwidth test");

            this.mlcCancellationTokenSource = new CancellationTokenSource();
            Task.Run(() =>
            {
                try
                {
                    StartMLCBandwidth(this.mlcCancellationTokenSource.Token, peakInjection);
                }
                catch (OperationCanceledException)
                {
                    this.KillMLC(null);
                }
                finally
                {
                    this.ResetBandwidthRunButton();
                }
            }, this.mlcCancellationTokenSource.Token);
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
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = this.mlcPath,
                    Arguments = mlcArguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true
            };
            lock (this.mlcProcessLock)
            {
                if (this.mlcProcessId != -1)
                {
                    return false;
                }
                this.logger.Log($"Running \"mlc {mlcArguments}\"");
                process.Start();
                this.mlcProcessId = process.Id;
            }
            cancelToken.ThrowIfCancellationRequested();

            int currRow = -1;
            ConsumeMLCOutput(process, cancelToken,
                delegate (string line)
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
                delegate (string line)
                {
                    string[] lineSplit = line.Split(new string[] { ":" }, StringSplitOptions.RemoveEmptyEntries);
                    if (lineSplit.Length > 1)
                    {
                        string bandwidth = lineSplit[lineSplit.Length - 1].Trim();
                        double bandwidthDouble = -1D;
                        if (!double.TryParse(bandwidth, out bandwidthDouble))
                        {
                            this.logger.Log(LogLevel.WARN, $"Found unknown output line: [{String.Join(", ", lineSplit)}]");
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
                                this.logger.Log(LogLevel.WARN,
                                    $"thread={Thread.CurrentThread.Name}: Failed to update user interface:",
                                    $"thread={Thread.CurrentThread.Name}: {ex.Message}"
                                );
                            }
                        }));
                    }
                    else
                    {
                        this.logger.Log(LogLevel.WARN, $"Found unknown output line: [{String.Join(", ", lineSplit)}]");
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
            this.KillMLC(process);
            if (currRow == -1)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    MessageBox.Show("Failed to fetch bandwidth data from Intel MLC. Please check the logs file for any errors.", "Error");
                }));
                return true;
            }
            return true;
        }

        // Run Latency

        private void BtnLatencyRun_Click(object sender, RoutedEventArgs e)
        {
            if (StopMLC())
            {
                return;
            }
            string validationResult = ValidateMLC();
            if (validationResult != null)
            {
                MessageBox.Show(validationResult, "Error");
                return;
            }

            this.ProgressLatency.Value = 0;
            for (int i = 0; i < INJECT_DELAYS.Length; i++)
            {
                LatencyRow RowLatency = (LatencyRow)this.GridLatency.Children[i + 1];
                RowLatency.InjectDelay = INJECT_DELAYS[i];
                RowLatency.Latency = "";
                RowLatency.Bandwidth = "";
            }
            this.BtnLatencyRun.Content = "Cancel";

            this.logger.Log("Running Intel MLC latency test");

            this.mlcCancellationTokenSource = new CancellationTokenSource();
            Task.Run(() =>
            {
                try
                {
                    StartMLCLatency(this.mlcCancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    this.KillMLC(null);
                }
                finally
                {
                    this.ResetLatencyRunButton();
                }
            }, this.mlcCancellationTokenSource.Token);
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
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = this.mlcPath,
                    Arguments = "--loaded_latency",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true
            };
            lock (this.mlcProcessLock)
            {
                if (this.mlcProcessId != -1)
                {
                    return false;
                }
                this.logger.Log("Running \"mlc --loaded_latency\"");
                process.Start();
                this.mlcProcessId = process.Id;
            }
            cancelToken.ThrowIfCancellationRequested();

            int currRow = -1;
            ConsumeMLCOutput(process, cancelToken,
                delegate (string line)
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
                delegate (string line)
                {
                    string[] lineSplit = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (lineSplit.Length == 3)
                    {
                        string injectDelay = lineSplit[0];
                        int injectDelayIndex = Array.IndexOf(INJECT_DELAYS, injectDelay);
                        if (injectDelayIndex == -1)
                        {
                            this.logger.Log(LogLevel.WARN, $"thread={Thread.CurrentThread.Name}: Skipping unknown inject delay = {injectDelay}");
                            currRow++;
                            return true;
                        }
                        int progressValue = (int)((injectDelayIndex + 1) * 100 / (double)INJECT_DELAYS.Length);
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                LatencyRow RowLatency = (LatencyRow)this.GridLatency.Children[injectDelayIndex + 1];
                                RowLatency.InjectDelay = lineSplit[0];
                                RowLatency.Latency = lineSplit[1];
                                RowLatency.Bandwidth = lineSplit[2];
                                this.ProgressLatency.Value = progressValue;
                            }
                            catch (Exception ex)
                            {
                                this.logger.Log(LogLevel.WARN,
                                    $"thread={Thread.CurrentThread.Name}: Failed to update user interface:",
                                    $"thread={Thread.CurrentThread.Name}: {ex.Message}"
                                );
                            }
                        }));
                    }
                    else
                    {
                        this.logger.Log(LogLevel.WARN, "Found unknown output line");
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
            this.KillMLC(process);
            if (currRow == -1)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    MessageBox.Show("Failed to fetch latency data from Intel MLC. Please check the logs file for any errors.", "Error");
                }));
                return true;
            }
            return true;
        }

        // Run Cache

        private void BtnCacheRun_Click(object sender, RoutedEventArgs e)
        {
            if (StopMLC())
            {
                return;
            }
            string validationResult = ValidateMLC();
            if (validationResult != null)
            {
                MessageBox.Show(validationResult, "Error");
                return;
            }

            this.TxtBoxL2Hit.Text = "";
            this.TxtBoxL2HitM.Text = "";
            this.BtnCacheRun.Content = "Cancel";

            this.logger.Log("Running Intel MLC cache test");

            this.mlcCancellationTokenSource = new CancellationTokenSource();
            Task.Run(() =>
            {
                try
                {
                    StartMLCCache(this.mlcCancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    this.KillMLC(null);
                }
                finally
                {
                    this.ResetCacheRunButton();
                }
            }, this.mlcCancellationTokenSource.Token);
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
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = this.mlcPath,
                    Arguments = "--c2c_latency",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true
            };
            lock (this.mlcProcessLock)
            {
                if (this.mlcProcessId != -1)
                {
                    return false;
                }
                this.logger.Log("Running \"mlc --c2c_latency\"");
                process.Start();
                this.mlcProcessId = process.Id;
            }
            cancelToken.ThrowIfCancellationRequested();

            int currRow = -1;
            ConsumeMLCOutput(process, cancelToken,
                delegate (string line)
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
                delegate (string line)
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
                                this.logger.Log(LogLevel.WARN,
                                    $"thread={Thread.CurrentThread.Name}: Failed to update user interface:",
                                    $"thread={Thread.CurrentThread.Name}: {ex.Message}"
                                );
                            }
                        }));
                    }
                    else
                    {
                        this.logger.Log(LogLevel.WARN, $"Found unknown output line: [{String.Join(", ", lineSplit)}]");
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
            this.KillMLC(process);
            if (currRow == -1)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    MessageBox.Show("Failed to fetch cache data from Intel MLC. Please check the logs file for any errors.", "Error");
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
                lock (this.mlcProcessLock)
                {
                    if (this.mlcProcessId != -1)
                    {
                        MessageBox.Show("Cannot modify MLC path whilst MLC is running.", "Error");
                        return;
                    }
                }

                this.TxtConfigureLog.ScrollToHome();
                this.TxtConfigureLog.Text = "";
                this.logger.Log("Downloading and extracting MLC...");
                this.WriteToConfigureLog(
                    $"Downloading Intel MLC...",
                    $"URL: {mlcUrl}",
                    $"Temporary destination: {tmpZipDestination}"
                );

                if (File.Exists(tmpZipDestination))
                {
                    this.logger.Log($"Deleting existing file at \"{tmpZipDestination}\"");
                    try
                    {
                        File.Delete(tmpZipDestination);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Error($"Failed to delete existing file at \"{tmpZipDestination}\":", ex);
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
                                this.logger.Log($"Failed to locate downloaded file at \"{tmpZipDestination}\"");
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
                                this.logger.Log($"Deleting existing temporary MLC directory at \"{extractedZipDirectory}\"");
                                FileUtils.Delete(extractedZipDirectory);
                            }
                            this.logger.Log($"File moved, extracting to \"{extractedZipDirectory}\"");
                            Dispatcher.Invoke(() =>
                            {
                                this.WriteToConfigureLog($"File moved, extracting to: {extractedZipDirectory}");
                            });
                            cancelToken = this.mlcDownloadCancellationSource.Token;
                            ExtractMLC(cancelToken, tmpZipDestination, extractedZipDirectory, () =>
                            {
                                try
                                {
                                    this.logger.Log($"Deleting \"{tmpZipDestination}\"...");
                                    FileUtils.Delete(tmpZipDestination);
                                    string mlcWindowsPath = Path.Combine(extractedZipDirectory, "Windows");
                                    string finalMLCPath = FileUtils.GetCurrentPath("mlc");
                                    if (!Directory.Exists(mlcWindowsPath))
                                    {
                                        this.logger.Log($"Failed to locate extracted directory at \"{mlcWindowsPath}\"");
                                        Dispatcher.Invoke(() =>
                                        {
                                            this.WriteToConfigureLog($"Failed to locate MLC at: {mlcWindowsPath}");
                                        });
                                        return;
                                    }
                                    if (FileUtils.DoesExist(finalMLCPath))
                                    {
                                        this.logger.Log($"Deleting existing MLC directory at \"{finalMLCPath}\"");
                                        FileUtils.Delete(finalMLCPath);
                                    }
                                    this.logger.Log($"Moving MLC from \"{mlcWindowsPath}\" to \"{finalMLCPath}\"");
                                    FileUtils.CopyAndMove(mlcWindowsPath, finalMLCPath);
                                    FileUtils.Delete(mlcWindowsPath);
                                    this.logger.Log($"Moved MLC to \"{finalMLCPath}\"");
                                    Dispatcher.Invoke(() =>
                                    {
                                        this.WriteToConfigureLog($"Extracted MLC to: {finalMLCPath}");
                                        this.WriteToConfigureLog("Success!");

                                        this.mlcPath = Path.Combine(finalMLCPath, "mlc.exe");
                                        this.TxtConfigurePath.Text = this.mlcPath;
                                        try
                                        {
                                            this.config.Set("mlcPath", this.mlcPath);
                                            this.config.Save();
                                        }
                                        catch (Exception ex)
                                        {
                                            this.logger.Error("Failed to update mlcPath in config:", ex);
                                        }
                                    });
                                }
                                catch (Exception ex)
                                {
                                    this.logger.Error($"Failed to extract {tmpZipDestination}:", ex);
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
                            this.logger.Error($"Failed to download MLC to {tmpZipDestination}:", ex);
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

        private async void DownloadMLC(CancellationToken cancelToken, string mlcUrl, string zipDestination, Runnable onComplete, Runnable onCancel, Runnable onFail)
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
                this.logger.Error("Failed to download MLC:", ex);
                if (onFail != null) onFail();
            }
        }

        private async void ExtractMLC(CancellationToken cancelToken, string zipFile, string destination, Runnable onComplete, Runnable onCancel, Runnable onFail, Runnable finalRunnable)
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
                this.logger.Error("Failed to extract MLC:", ex);
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
            lock (this.mlcProcessLock)
            {
                if (this.mlcProcessId != -1)
                {
                    MessageBox.Show("Cannot modify MLC path whilst MLC is running.", "Error");
                    return;
                }
            }
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                if (!openFileDialog.CheckFileExists)
                {
                    MessageBox.Show("Please select a valid exe.", "Error");
                    return;
                }
                this.mlcPath = openFileDialog.FileName;
                this.TxtConfigurePath.Text = this.mlcPath;
                try
                {
                    this.config.Set("mlcPath", openFileDialog.FileName);
                    this.config.Save();
                    this.logger.Log($"Updated mlcPath to: {this.mlcPath}");
                }
                catch (Exception ex)
                {
                    this.logger.Error("Failed to save mlcPath to config:", ex);
                }
            }
        }

        private void TxtConfigurePath_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                lock (this.mlcProcessLock)
                {
                    if (this.mlcProcessId != -1)
                    {
                        MessageBox.Show("Cannot modify MLC path whilst MLC is running.", "Error");
                        return;
                    }
                }
                this.mlcPath = this.TxtConfigurePath.Text.Trim();
                try
                {
                    this.config.Set("mlcPath", this.mlcPath);
                    this.config.Save();
                    this.logger.Log($"Updated mlcPath manually to: {this.mlcPath}");
                }
                catch (Exception ex)
                {
                    this.logger.Error("Failed to save mlcPath to config:", ex);
                }
            }
        }
    }
}
