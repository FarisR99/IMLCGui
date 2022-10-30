using Octokit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IMLCGui
{
    internal class AutoUpdater
    {
        private readonly GitHubClient _client;
        public readonly Version CurrentVersion;

        public Release LatestRelease { get; private set; } = null;
        public volatile bool CheckingForUpdate = false;

        public AutoUpdater()
        {
            this._client = new GitHubClient(new ProductHeaderValue("IMLCGui"));
            this._client.SetRequestTimeout(TimeSpan.FromSeconds(2));

            this.CurrentVersion = GetCurrentVersion();
        }

        public async Task CheckForUpdates()
        {
            this.LatestRelease = null;
            this.CheckingForUpdate = true;

            IReadOnlyList<Release> releases = null;
            try
            {
                releases = await this._client.Repository.Release.GetAll("FarisR99", "IMLCGui");
            }
            catch (Exception)
            {
                this.CheckingForUpdate = false;
                return;
            }
            if (releases == null || releases.Count == 0)
            {
                this.CheckingForUpdate = false;
                return;
            }

            foreach (var release in releases)
            {
                if (!release.Prerelease)
                {
                    this.LatestRelease = release;
                    break;
                }
            }
            this.CheckingForUpdate = false;
        }

        public bool HasUpdateAvailable()
        {
            if (this.CurrentVersion == null) return true;
            if (this.LatestRelease == null) return false;
            Version latestReleaseVersion = new Version(GetLatestReleaseVersion());
            return latestReleaseVersion.CompareTo(this.CurrentVersion) > 0;
        }

        public string GetLatestReleaseVersion()
        {
            if (this.LatestRelease == null) return null;
            return this.LatestRelease.TagName;
        }

        public async Task DownloadLatest(Logger logger)
        {
            string latestReleaseVersion = GetLatestReleaseVersion();
            logger.Log($"Downloading latest IMLCGui version {FormatVersion(latestReleaseVersion)}...");
            string outputFile = await DownloadService.DownloadFileAsync(null, FormatGithubAssetUrl(latestReleaseVersion), $"IMLCGui-{latestReleaseVersion}.exe", null);
            logger.Log($"Downloaded latest version to {outputFile}!");
        }

        public static Version GetCurrentVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            if (assembly == null) return null;
            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            if (fileVersionInfo == null) return null;
            return fileVersionInfo.ProductVersion != null
                ? new Version(fileVersionInfo.ProductVersion)
                : (fileVersionInfo.FileVersion != null
                    ? new Version(fileVersionInfo.FileVersion)
                    : null);
        }

        public static string FormatGithubAssetUrl(string tag)
        {
            return $"https://github.com/FarisR99/IMLCGui/releases/download/{tag}/IMLCGui.exe";
        }

        public static string FormatVersion(string version)
        {
            if (version == null) return null;
            if (version.Split(new char[] { '.' }).Length == 3)
            {
                version += ".0";
            }
            return version;
        }
    }
}
