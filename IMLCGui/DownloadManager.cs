using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace IMLCGui
{
    // Taken from https://stackoverflow.com/questions/49034241/canceling-webclient-download-while-waiting-for-download-to-complete
    internal class DownloadService
    {
        public static async Task<string> DownloadFileAsync(CancellationToken? cancellationToken, string url, string outputFileName, DownloadProgressChangedEventHandler handler)
        {
            using (var webClient = new WebClient())
            {
                if (cancellationToken.HasValue)
                {
                    cancellationToken.Value.Register(webClient.CancelAsync);
                }

                try
                {
                    if (handler != null)
                    {
                        webClient.DownloadProgressChanged += handler;
                    }
                    var task = webClient.DownloadFileTaskAsync(url, outputFileName);
                    await task;
                }
                catch (WebException ex) when (ex.Status == WebExceptionStatus.RequestCanceled)
                {
                    throw new OperationCanceledException();
                }
                catch (AggregateException ex) when (ex.InnerException is WebException exWeb && exWeb.Status == WebExceptionStatus.RequestCanceled)
                {
                    throw new OperationCanceledException();
                }
                catch (TaskCanceledException)
                {
                    throw new OperationCanceledException();
                }

                return outputFileName;
            }
        }

        public static async Task ExtractTGZ(CancellationToken cancellationToken, string gzArchiveName, string destFolder)
        {
            using (Stream inStream = File.OpenRead(gzArchiveName))
            {
                using (Stream gzipStream = new GZipInputStream(inStream))
                {
                    using (TarArchive tarArchive = TarArchive.CreateInputTarArchive(gzipStream))
                    {
                        cancellationToken.Register(tarArchive.Close);
                        await Task.Run(() => tarArchive.ExtractContents(destFolder), cancellationToken);
                    }
                }
            }
        }
    }
}
