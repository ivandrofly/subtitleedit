using System;
using System.Net;
using System.Net.Http;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Core.Http
{
    public static class DownloaderFactory
    {
        public static IDownloader Create()
        {
            var httpClient = HttpClientFactory.Create();
            if (Configuration.Settings.General.UseLegacyDownloader)
            {
                return new LegacyDownloader(httpClient);
            }

            return new HttpClientDownloader(httpClient);
        }
    }
}
