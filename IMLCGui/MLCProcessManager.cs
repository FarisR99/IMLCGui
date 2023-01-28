using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IMLCGui
{
    internal class MLCProcessManager
    {
        private Logger _logger;

        public string Path = "";
        private string Version = null;

        public object ProcessLock { get; } = new object();
        public CancellationTokenSource CancellationTokenSource;
        public int ProcessId { get; private set; } = -1;

        public MLCProcessManager(Logger logger, string path)
        {
            this._logger = logger;
            this.Path = path;
        }

        public void CancelToken()
        {
            if (this.CancellationTokenSource != null)
            {
                this.CancellationTokenSource.Cancel();
                this.CancellationTokenSource = null;
            }
        }

        private Process CreateProcess(string arguments)
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = this.Path,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true
            };
        }

        public void ConsumeOutput(Process process, CancellationToken cancelToken, Predicate<string> shouldStartProcessing, Func<string, bool> processOutputLine)
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
                if (this._logger != null)
                {
                    this._logger.Log(LogLevel.PROCESS, line);
                }
                if (shouldProcessLine)
                {
                    if (!processOutputLine(line))
                    {
                        break;
                    }
                }
                else if (shouldStartProcessing(line))
                {
                    shouldProcessLine = true;
                }
            }
        }

        public string GetVersion()
        {
            return this.Version;
        }

        public void FetchVersion()
        {
            if (this.Path == null)
            {
                this.Version = null;
                return;
            }
            string mlcPath = this.Path;
            if (mlcPath.Trim().Length == 0)
            {
                mlcPath = FileUtils.GetCurrentPath("mlc.exe");
                if (!FileUtils.DoesExist(mlcPath))
                {
                    this.Version = null;
                    return;
                }
            }
            Task.Run(async () =>
            {
                this.Version = await MLCProcess.GetMLCVersion(mlcPath);
                if (this.Version != null)
                {
                    this.Version = this.Version.Trim();
                }
                Console.Out.WriteLine("Detected MLC version: " + this.Version);
            });
        }

        public void Kill()
        {
            this.Kill(null);
        }

        public void Kill(Process process)
        {
            this.Kill(process, true);
        }

        public void Kill(Process process, bool destroyCancelTokenSource)
        {
            lock (this.ProcessLock)
            {
                if (process == null)
                {
                    if (this.ProcessId == -1)
                    {
                        return;
                    }
                    process = Process.GetProcessById(this.ProcessId);
                    if (process == null)
                    {
                        if (this._logger != null)
                        {
                            this._logger.Warn($"Process with ID {this.ProcessId} is not running.");
                        }
                        this.ProcessId = -1;
                        if (destroyCancelTokenSource)
                        {
                            this.CancellationTokenSource = null;
                        }
                        return;
                    }
                }
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex)
                    {
                        if (this._logger != null)
                        {
                            this._logger.Error($"Failed to kill {process.ProcessName}:", ex);
                        }
                    }
                }
                this.ProcessId = -1;
                if (destroyCancelTokenSource)
                {
                    this.CancellationTokenSource = null;
                }
            }
        }

        public Process StartProcess(string arguments)
        {
            Process process = this.CreateProcess(arguments);
            lock (this.ProcessLock)
            {
                if (this.ProcessId != -1)
                {
                    return null;
                }
                if (this._logger != null)
                {
                    this._logger.Log($"Running \"mlc {arguments}\"");
                }
                process.Start();
                this.ProcessId = process.Id;

                if (Process.GetCurrentProcess().PriorityClass.CompareTo(ProcessPriorityClass.AboveNormal) >= 0)
                {
                    process.PriorityClass = ProcessPriorityClass.RealTime;
                }
                else
                {
                    process.PriorityClass = ProcessPriorityClass.AboveNormal;
                }
            }
            return process;
        }

        public bool Stop()
        {
            bool stopped = false;
            if (this.CancellationTokenSource != null && !this.CancellationTokenSource.IsCancellationRequested)
            {
                this.CancellationTokenSource.Cancel();
                this.CancellationTokenSource = null;
                stopped = true;
                try
                {
                    Thread.Sleep(250);
                }
                catch
                {
                }
            }
            lock (this.ProcessLock)
            {
                if (this.ProcessId != -1)
                {
                    try
                    {
                        if (this._logger != null)
                        {
                            this._logger.Debug("User requested cancellation, killing MLC...");
                        }
                        Process runningProcess = Process.GetProcessById(this.ProcessId);
                        if (runningProcess != null && !runningProcess.HasExited)
                        {
                            string processName = runningProcess.ProcessName;
                            runningProcess.Kill();
                            runningProcess.WaitForExit();
                            if (this._logger != null)
                            {
                                this._logger.Log($"Killed process: {processName}");
                            }
                        }
                        else
                        {
                            if (this._logger != null)
                            {
                                this._logger.Log("Killed MLC.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (this._logger != null)
                        {
                            this._logger.Error("Failed to kill MLC process:", ex);
                        }
                    }
                    finally
                    {
                        this.ProcessId = -1;
                    }
                    stopped = true;
                }
            }
            return stopped;
        }
    }
}
