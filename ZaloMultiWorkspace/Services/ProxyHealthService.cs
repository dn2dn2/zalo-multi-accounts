using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ZaloMultiWorkspace.Models;

namespace ZaloMultiWorkspace.Services
{
    public class ProxyHealthResult
    {
        public bool IsHealthy { get; set; }
        public long PingMs { get; set; } = -1;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class ProxyHealthService
    {
        public async Task<ProxyHealthResult> CheckProxyAsync(ZaloAccountProfile profile, CancellationToken cancellationToken = default)
        {
            // Nếu không cấu hình Proxy thì đo ping trực tiếp tới chat.zalo.me
            if (!profile.HasProxy)
            {
                return await MeasureDirectPingAsync(cancellationToken);
            }

            string host = profile.ProxyHost;
            int port = profile.ProxyPort;
            string type = string.IsNullOrWhiteSpace(profile.ProxyType) ? "http" : profile.ProxyType.ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(profile.ProxyAddress))
            {
                var parts = profile.ProxyAddress.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out int parsedPort))
                {
                    host = parts[0];
                    port = parsedPort;
                }
            }

            if (string.IsNullOrWhiteSpace(host) || port <= 0)
            {
                return new ProxyHealthResult
                {
                    IsHealthy = false,
                    PingMs = -1,
                    ErrorMessage = "Địa chỉ Proxy hoặc cổng (Port) không hợp lệ."
                };
            }

            try
            {
                var proxyUri = new Uri($"{type}://{host}:{port}");
                var webProxy = new WebProxy(proxyUri);

                if (!string.IsNullOrWhiteSpace(profile.ProxyUser))
                {
                    webProxy.Credentials = new NetworkCredential(profile.ProxyUser, profile.ProxyPass);
                }

                using var handler = new HttpClientHandler
                {
                    Proxy = webProxy,
                    UseProxy = true,
                    PreAuthenticate = true
                };

                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(8)
                };

                var sw = Stopwatch.StartNew();
                using var request = new HttpRequestMessage(HttpMethod.Head, "https://chat.zalo.me");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                sw.Stop();

                return new ProxyHealthResult
                {
                    IsHealthy = true,
                    PingMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                return new ProxyHealthResult
                {
                    IsHealthy = false,
                    PingMs = -1,
                    ErrorMessage = $"Không thể kết nối qua Proxy ({host}:{port}): {ex.Message}"
                };
            }
        }

        private async Task<ProxyHealthResult> MeasureDirectPingAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(5)
                };
                var sw = Stopwatch.StartNew();
                using var request = new HttpRequestMessage(HttpMethod.Head, "https://chat.zalo.me");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                sw.Stop();

                return new ProxyHealthResult
                {
                    IsHealthy = true,
                    PingMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                return new ProxyHealthResult
                {
                    IsHealthy = false,
                    PingMs = -1,
                    ErrorMessage = $"Lỗi mạng khi truy cập Zalo Web: {ex.Message}"
                };
            }
        }
    }
}

