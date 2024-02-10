using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace IMLCGui
{
    internal class MLCProcess
    {
        private static readonly string VERSION_STRING_PREFIX = "Memory Latency Checker - ";
        private static readonly List<string> NEW_VERSIONS = new List<string> { "v3.10", "v3.11" };

        public static async Task<string> GetMLCVersion(string processPath)
        {
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = processPath,
                    Arguments = "--invalid_argument", // Use an invalid argument just to print the version
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true
            };
            process.Start();

            string version = null;
            while (!process.StandardOutput.EndOfStream)
            {
                string line;
                try
                {
                    line = process.StandardOutput.ReadLine();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    break;
                }
                if (line.Contains(VERSION_STRING_PREFIX))
                {
                    version = line.Substring(line.LastIndexOf(VERSION_STRING_PREFIX) + VERSION_STRING_PREFIX.Length);
                    break;
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
                    Console.Error.WriteLine(ex.Message);
                }
            }
            return version;
        }

        public static string GenerateBandwidthArguments(string mlcVersion, bool peakInjection)
        {
            return peakInjection ? "--peak_injection_bandwidth" : "--max_bandwidth";
        }

        public static string GenerateCacheArguments(string mlcVersion)
        {
            string mlcArguments = "--c2c_latency";
            if (NEW_VERSIONS.Contains(mlcVersion))
            {
                mlcArguments += " -e0";
            }
            return mlcArguments;
        }

        public static string GenerateLatencyArguments(string mlcVersion, string injectDelayOverride)
        {
            string mlcArguments = "--loaded_latency";
            if (injectDelayOverride != null)
            {
                mlcArguments += " -d" + injectDelayOverride;
            }
            if (NEW_VERSIONS.Contains(mlcVersion))
            {
                mlcArguments += " -e0";
            }
            return mlcArguments;
        }

        public static string GenerateQuickBandwidthArguments(string mlcVersion)
        {
            return "--bandwidth_matrix";
        }

        public static string GenerateQuickLatencyArguments(string mlcVersion)
        {
            string mlcArguments = "--latency_matrix";
            if (NEW_VERSIONS.Contains(mlcVersion))
            {
                mlcArguments += " -e0";
            }
            return mlcArguments;
        }
    }
}
