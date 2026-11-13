using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZaloMultiWorkspace.Models;

namespace ZaloMultiWorkspace.Services
{
    /// <summary>
    /// Ảnh avatar người gửi cho thông báo. Ưu tiên ảnh trang chat gửi sang (data URL, đã đi qua proxy của tài khoản);
    /// nếu trang không đọc được (ảnh khác tên miền), tải bằng HttpClient qua đúng proxy của tài khoản.
    /// Giữ tối đa ~60 ảnh gần nhất trong bộ nhớ.
    /// </summary>
    public static class AvatarFetcher
    {
        private const int MaxCache = 60;
        private const int MaxBytes = 300_000;
        private static readonly Dictionary<string, ImageSource> _cache = new();
        private static readonly LinkedList<string> _order = new();

        private static void Remember(string key, ImageSource img)
        {
            if (_cache.ContainsKey(key))
                return;
            _cache[key] = img;
            _order.AddLast(key);
            while (_order.Count > MaxCache)
            {
                _cache.Remove(_order.First!.Value);
                _order.RemoveFirst();
            }
        }

        public static ImageSource? FromBytes(byte[] bytes)
        {
            try
            {
                if (bytes.Length == 0 || bytes.Length > MaxBytes)
                    return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 96; // thông báo chỉ hiện 44px -> giải mã nhỏ cho nhẹ
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Ảnh từ data URL do trang chat gửi sang</summary>
        public static ImageSource? FromDataUrl(string? dataUrl)
        {
            if (string.IsNullOrEmpty(dataUrl) || !dataUrl.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                return null;
            if (_cache.TryGetValue(dataUrl.Length + ":" + dataUrl.GetHashCode(), out var cached))
                return cached;
            int comma = dataUrl.IndexOf(',');
            if (comma < 0 || !dataUrl.Substring(0, comma).Contains(";base64", StringComparison.OrdinalIgnoreCase))
                return null;
            try
            {
                var img = FromBytes(Convert.FromBase64String(dataUrl[(comma + 1)..]));
                if (img != null)
                    Remember(dataUrl.Length + ":" + dataUrl.GetHashCode(), img);
                return img;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Tải ảnh http(s) qua proxy của tài khoản (không lộ IP thật khi tài khoản dùng proxy)</summary>
        public static async Task<ImageSource?> FetchAsync(ZaloAccountProfile profile, string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return null;
            string key = profile.ProxySignature + "|" + url;
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            try
            {
                using var handler = new HttpClientHandler();
                if (profile.HasProxy)
                {
                    if (profile.IsUnsupportedSocks5Auth)
                        return null; // không đi thẳng ra mạng thay cho proxy
                    string host = profile.ProxyHost;
                    int port = profile.ProxyPort;
                    if ((string.IsNullOrWhiteSpace(host) || port <= 0) && !string.IsNullOrWhiteSpace(profile.ProxyAddress))
                    {
                        var parts = profile.ProxyAddress.Split(':', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && int.TryParse(parts[1], out int p)) { host = parts[0]; port = p; }
                    }
                    string type = string.IsNullOrWhiteSpace(profile.ProxyType) ? "http" : profile.ProxyType.ToLowerInvariant();
                    var proxy = new WebProxy(new Uri($"{type}://{host}:{port}"));
                    if (!string.IsNullOrWhiteSpace(profile.ProxyUser))
                        proxy.Credentials = new NetworkCredential(profile.ProxyUser, profile.ProxyPass);
                    handler.Proxy = proxy;
                    handler.UseProxy = true;
                }
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/*");
                using var resp = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode || (resp.Content.Headers.ContentLength ?? 0) > MaxBytes)
                    return null;
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                var img = FromBytes(bytes);
                if (img != null)
                    Remember(key, img);
                return img;
            }
            catch
            {
                return null;
            }
        }
    }
}
