using System;
using System.Net;
using System.Net.Http;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Core.Http
{
    /// <summary>
    /// Represents a factory for creating instances of HttpClient with default or provided proxy settings.
    /// </summary>
    public class HttpClientFactory
    {
        /// <summary>
        /// Creates an instance of HttpClient with default or provided proxy settings.
        /// </summary>
        /// <returns>An instance of HttpClient.</returns>
        public static HttpClient Create() => Create(Configuration.Settings.Proxy);

        /// <summary>
        /// Creates an instance of HttpClient with default or provided proxy settings.
        /// </summary>
        /// <returns>An instance of HttpClient.</returns>
        public static HttpClient Create(ProxySettings proxySettings) => new HttpClient(CreateHandler(proxySettings));

        /// <summary>
        /// Creates an instance of HttpClientHandler with default or provided proxy settings.
        /// </summary>
        /// <param name="proxySettings">The proxy settings to be used.</param>
        /// <returns>An instance of HttpClientHandler.</returns>
        public static HttpClientHandler CreateHandler(ProxySettings proxySettings)
        {
            var handler = new HttpClientHandler();

            if (!string.IsNullOrEmpty(proxySettings.ProxyAddress))
            {
                handler.Proxy = new WebProxy(proxySettings.ProxyAddress);
                handler.UseProxy = true;
            }

            if (proxySettings.UseDefaultCredentials)
            {
                handler.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;
                handler.Credentials = CredentialCache.DefaultNetworkCredentials;
            }
            else if (!string.IsNullOrEmpty(proxySettings.UserName) && !string.IsNullOrEmpty(proxySettings.ProxyAddress))
            {
                var networkCredential = string.IsNullOrWhiteSpace(proxySettings.Domain) ? new NetworkCredential(proxySettings.UserName, proxySettings.Password) : new NetworkCredential(proxySettings.UserName, proxySettings.Password, proxySettings.Domain);
                var credentialCache = new CredentialCache
                {
                    {
                        new Uri(proxySettings.ProxyAddress),
                        proxySettings.AuthType,
                        networkCredential
                    }
                };
                handler.Credentials = credentialCache;
            }

            return handler;
        }
    }
}