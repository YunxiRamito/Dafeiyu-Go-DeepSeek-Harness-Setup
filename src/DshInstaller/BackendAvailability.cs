using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

namespace DshInstaller
{
    internal enum BackendConnectionState { Connecting, Online, Offline }

    internal static class BackendAvailability
    {
        internal static async Task<bool> ProbeAsync(CancellationToken cancellation)
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            ProxySupport.Apply(handler);
            BackendDownloadSource.Apply(handler);
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };
            using var response = await http.GetAsync(BackendDownloadSource.BaseUrl + "/health/ready",
                HttpCompletionOption.ResponseHeadersRead, cancellation);
            return response.IsSuccessStatusCode;
        }
    }
}
