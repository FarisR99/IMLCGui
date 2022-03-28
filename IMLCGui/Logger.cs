using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace IMLCGui
{
    public class Logger
    {
        private static List<string> PATHS_IN_USE = new List<string>();

        private string FilePath;
        private ReaderWriterLock locker = new ReaderWriterLock();

        public Logger(string FilePath)
        {
            if (PATHS_IN_USE.Contains(FilePath))
            {
                throw new Exception("Log file path already in use");
            }
            PATHS_IN_USE.Add(FilePath);
            this.FilePath = FilePath;
        }

        public void Close()
        {
            PATHS_IN_USE.Remove(FilePath);
        }

        public void Debug(params string[] logMessages)
        {
            Log(LogLevel.DEBUG, logMessages);
        }

        public void Error(string errorMessage)
        {
            Log(LogLevel.ERROR, errorMessage);
        }

        public void Error(Exception ex)
        {
            Log(LogLevel.ERROR, ex.Message);
        }

        public void Error(string errorMessage, Exception ex)
        {
            Log(LogLevel.ERROR, errorMessage, ex.Message);
        }

        public void Log(params string[] logMessages)
        {
            Log(LogLevel.INFO, logMessages);
        }

        public void Warn(params string[] logMessages)
        {
            Log(LogLevel.WARN, logMessages);
        }

        public void Log(LogLevel level, params string[] logMessages)
        {
            try
            {
                try
                {
                    this.locker.AcquireWriterLock(1000);
                    using (StreamWriter w = File.AppendText(this.FilePath))
                    {
                        foreach (string message in logMessages)
                        {
                            LogLine(w, level, message);
                        }
                    }
                }
                catch
                {
                    foreach (string message in logMessages)
                    {
                        LogLine(null, level, message);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
            finally
            {
                this.locker.ReleaseWriterLock();
            }
        }

        private static void LogLine(TextWriter w, LogLevel level, object logMessage)
        {
            string formattedMsg = $"[{DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()}] [{level}]: {logMessage}";
            if (level == LogLevel.ERROR)
            {
                Console.Error.WriteLine(formattedMsg);
            }
            else
            {
                Console.WriteLine(formattedMsg);
            }
            if (w != null)
            {
                w.WriteLine(formattedMsg);
            }
        }
    }

    public enum LogLevel
    {
        DEBUG, INFO, WARN, ERROR, PROCESS
    }
}
