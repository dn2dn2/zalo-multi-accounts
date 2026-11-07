using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace ZaloMultiWorkspace.Services
{
    public enum TranslationProvider
    {
        GoogleFree = 0,
        /// <summary>Cũ (bản trước) - tự chuyển sang Ai khi mở Cài đặt</summary>
        Gemini = 1,
        /// <summary>Cũ (bản trước) - tự chuyển sang Ai khi mở Cài đặt</summary>
        DeepSeek = 2,
        /// <summary>Dịch bằng AI qua API URL + khóa + mô hình (Gemini gốc hoặc chuẩn tương thích OpenAI)</summary>
        Ai = 3
    }

    /// <summary>Nhà cung cấp AI dựng sẵn: điền nhanh URL và mô hình</summary>
    public record AiPreset(string Id, string Name, string Url, string Model)
    {
        public override string ToString() => Name;
    }

    /// <param name="Name">Tên đầy đủ (danh sách thả xuống)</param>
    /// <param name="Short">Tên ngắn hiển thị trong ô đã chọn</param>
    /// <param name="Native">Tên bản ngữ cho nút ghim (English, 简体中文...)</param>
    public record LanguageInfo(string Code, string Name, string Short, string Native)
    {
        public override string ToString() => Short;
    }

    public class TranslationResult
    {
        public bool Success { get; init; }
        public string Text { get; init; } = string.Empty;
        /// <summary>Mã ngôn ngữ nguồn nhận diện được (khi chọn Tự nhận diện)</summary>
        public string? DetectedLanguage { get; init; }
        public string Error { get; init; } = string.Empty;
        /// <summary>Lỗi do dịch vụ AI (khóa, hạn mức...) - có thể thử lại bằng Google Dịch</summary>
        public bool CanFallbackToGoogle { get; init; }

        public static TranslationResult Fail(string error, bool canFallback = false) =>
            new() { Success = false, Error = error, CanFallbackToGoogle = canFallback };
    }

    /// <summary>
    /// Dịch văn bản qua Google Dịch (cổng không chính thức, không cần khóa), Gemini hoặc DeepSeek (khóa API).
    /// Chỉ gọi khi người dùng bấm Dịch. Gửi trực tiếp từ máy người dùng tới dịch vụ qua HTTPS.
    /// </summary>
    public class TranslationService
    {
        public const string AutoDetect = "auto";
        public const string DefaultGeminiModel = "gemini-2.5-flash";
        public const string DefaultDeepSeekModel = "deepseek-chat";

        /// <summary>Nhà cung cấp dựng sẵn. URL Gemini có chứa sẵn tên mô hình (cổng generateContent).</summary>
        public static readonly IReadOnlyList<AiPreset> AiPresets = new List<AiPreset>
        {
            new("DeepSeek", "DeepSeek", "https://api.deepseek.com", "deepseek-chat"),
            new("Gemini", "Gemini", "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent", ""),
            new("OpenAI", "OpenAI (ChatGPT)", "https://api.openai.com/v1", ""),
            new("OpenRouter", "OpenRouter", "https://openrouter.ai/api/v1", ""),
            new("Custom", "Khác (tự nhập)", "", "")
        };

        /// <summary>URL là cổng riêng của Gemini (generateContent) chứ không phải chuẩn OpenAI</summary>
        public static bool IsGeminiNativeUrl(string? url) =>
            !string.IsNullOrWhiteSpace(url) &&
            url.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("/openai", StringComparison.OrdinalIgnoreCase);

        /// <summary>Tên mô hình nằm trong URL Gemini (…/models/{model}:generateContent), nếu có</summary>
        public static string? ModelFromGeminiUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(url, @"/models/([^/:?]+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Tên hiển thị của dịch vụ theo URL (dùng trong thông báo lỗi)</summary>
        public static string ServiceNameFromUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "Dịch vụ AI";
            if (IsGeminiNativeUrl(url) || url.Contains("googleapis", StringComparison.OrdinalIgnoreCase)) return "Gemini";
            if (url.Contains("deepseek", StringComparison.OrdinalIgnoreCase)) return "DeepSeek";
            if (url.Contains("openai.com", StringComparison.OrdinalIgnoreCase)) return "OpenAI";
            if (url.Contains("openrouter", StringComparison.OrdinalIgnoreCase)) return "OpenRouter";
            try { return new Uri(url).Host; } catch { return "Dịch vụ AI"; }
        }

        /// <summary>Danh sách ngôn ngữ (mã theo chuẩn Google Dịch)</summary>
        public static readonly IReadOnlyList<LanguageInfo> Languages = new List<LanguageInfo>
        {
            new("vi", "Tiếng Việt", "Tiếng Việt", "Tiếng Việt"),
            new("en", "Tiếng Anh · English", "Tiếng Anh", "English"),
            new("zh-CN", "Tiếng Trung (Giản thể) · 简体中文", "Trung giản thể", "简体中文"),
            new("zh-TW", "Tiếng Trung (Phồn thể) · 繁體中文", "Trung phồn thể", "繁體中文"),
            new("ja", "Tiếng Nhật · 日本語", "Tiếng Nhật", "日本語"),
            new("ko", "Tiếng Hàn · 한국어", "Tiếng Hàn", "한국어"),
            new("th", "Tiếng Thái · ไทย", "Tiếng Thái", "ไทย"),
            new("km", "Tiếng Khmer · ខ្មែរ", "Tiếng Khmer", "ខ្មែរ"),
            new("lo", "Tiếng Lào · ລາວ", "Tiếng Lào", "ລາວ"),
            new("my", "Tiếng Myanmar · မြန်မာ", "Tiếng Myanmar", "မြန်မာ"),
            new("id", "Tiếng Indonesia · Bahasa Indonesia", "Indonesia", "Indonesia"),
            new("ms", "Tiếng Mã Lai · Bahasa Melayu", "Mã Lai", "Melayu"),
            new("tl", "Tiếng Philippines · Filipino", "Philippines", "Filipino"),
            new("hi", "Tiếng Hindi · हिन्दी", "Tiếng Hindi", "हिन्दी"),
            new("bn", "Tiếng Bengal · বাংলা", "Tiếng Bengal", "বাংলা"),
            new("ar", "Tiếng Ả Rập · العربية", "Tiếng Ả Rập", "العربية"),
            new("tr", "Tiếng Thổ Nhĩ Kỳ · Türkçe", "Thổ Nhĩ Kỳ", "Türkçe"),
            new("ru", "Tiếng Nga · Русский", "Tiếng Nga", "Русский"),
            new("uk", "Tiếng Ukraina · Українська", "Ukraina", "Українська"),
            new("fr", "Tiếng Pháp · Français", "Tiếng Pháp", "Français"),
            new("de", "Tiếng Đức · Deutsch", "Tiếng Đức", "Deutsch"),
            new("es", "Tiếng Tây Ban Nha · Español", "Tây Ban Nha", "Español"),
            new("pt", "Tiếng Bồ Đào Nha · Português", "Bồ Đào Nha", "Português"),
            new("it", "Tiếng Ý · Italiano", "Tiếng Ý", "Italiano"),
            new("nl", "Tiếng Hà Lan · Nederlands", "Hà Lan", "Nederlands"),
            new("pl", "Tiếng Ba Lan · Polski", "Ba Lan", "Polski"),
            new("cs", "Tiếng Séc · Čeština", "Tiếng Séc", "Čeština"),
            new("sv", "Tiếng Thụy Điển · Svenska", "Thụy Điển", "Svenska")
        };

        /// <summary>Danh sách cho ô "Từ": có thêm Tự nhận diện ở đầu</summary>
        public static readonly IReadOnlyList<LanguageInfo> SourceLanguages =
            new[] { new LanguageInfo(AutoDetect, "Tự nhận diện", "Tự nhận diện", "Tự nhận diện") }.Concat(Languages).ToList();

        public static string NameOf(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;
            var hit = Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
                      ?? Languages.FirstOrDefault(l => code.StartsWith(l.Code.Split('-')[0], StringComparison.OrdinalIgnoreCase));
            return hit?.Name ?? code;
        }

        /// <summary>Tên ngôn ngữ tiếng Anh để đưa vào chỉ dẫn cho AI</summary>
        private static string EnglishName(string code) => code switch
        {
            "vi" => "Vietnamese", "en" => "English", "zh-CN" => "Simplified Chinese", "zh-TW" => "Traditional Chinese",
            "ja" => "Japanese", "ko" => "Korean", "th" => "Thai", "km" => "Khmer", "lo" => "Lao", "my" => "Burmese",
            "id" => "Indonesian", "ms" => "Malay", "tl" => "Filipino", "hi" => "Hindi", "bn" => "Bengali",
            "ar" => "Arabic", "tr" => "Turkish", "ru" => "Russian", "uk" => "Ukrainian", "fr" => "French",
            "de" => "German", "es" => "Spanish", "pt" => "Portuguese", "it" => "Italian", "nl" => "Dutch",
            "pl" => "Polish", "cs" => "Czech", "sv" => "Swedish", _ => code
        };

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>Dịch bằng Google Dịch (provider = GoogleFree) hoặc AI qua API URL</summary>
        public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang,
            TranslationProvider provider, string apiUrl, string apiKey, string model, string? prompt = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return TranslationResult.Fail("Chưa có nội dung để dịch.");

            bool isAi = provider != TranslationProvider.GoogleFree;
            try
            {
                return isAi
                    ? await TranslateAiAsync(text, sourceLang, targetLang, apiUrl, apiKey, model, prompt, ct)
                    : await TranslateGoogleAsync(text, sourceLang, targetLang, ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return TranslationResult.Fail("Dịch vụ phản hồi quá lâu. Thử lại sau.", isAi);
            }
            catch (HttpRequestException ex)
            {
                return TranslationResult.Fail($"Không kết nối được dịch vụ dịch ({ex.Message}). Kiểm tra mạng và API URL.", isAi);
            }
            catch (UriFormatException)
            {
                return TranslationResult.Fail("API URL không hợp lệ. Kiểm tra lại trong Cài đặt → Dịch.", isAi);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or NullReferenceException)
            {
                return TranslationResult.Fail("Dịch vụ trả về kết quả không đọc được. Thử lại.", isAi);
            }
        }

        // ---------------- Google Dịch (không cần khóa) ----------------
        private static async Task<TranslationResult> TranslateGoogleAsync(string text, string src, string dst, CancellationToken ct)
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t"
                         + $"&sl={Uri.EscapeDataString(src)}&tl={Uri.EscapeDataString(dst)}&q={Uri.EscapeDataString(text)}";
            using var resp = await Http.GetAsync(url, ct);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests || resp.StatusCode == HttpStatusCode.Forbidden)
                return TranslationResult.Fail("Google Dịch đang giới hạn số lần dịch. Thử lại sau ít phút hoặc dùng Gemini/DeepSeek.");
            if (!resp.IsSuccessStatusCode)
                return TranslationResult.Fail($"Google Dịch báo lỗi {(int)resp.StatusCode}.");

            var root = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))!.AsArray();
            var sb = new StringBuilder();
            foreach (var seg in root[0]!.AsArray())
            {
                var part = seg?[0]?.GetValue<string>();
                if (part != null) sb.Append(part);
            }
            string? detected = root.Count > 2 ? root[2]?.GetValue<string>() : null;
            return new TranslationResult { Success = true, Text = sb.ToString().Trim(), DetectedLanguage = detected };
        }

        // ---------------- Prompt cho AI ----------------
        /// <summary>Prompt mặc định (người dùng sửa được trong Cài đặt → Dịch)</summary>
        public const string DefaultPrompt =
            "Bạn là phiên dịch cho nhân viên chăm sóc khách hàng đang chat với khách hàng và nhà cung cấp.\n" +
            "Quy tắc:\n" +
            "- Giữ nguyên số, giá tiền, đơn vị tiền tệ, đơn vị đo, ngày tháng, mã sản phẩm, đường link, emoji và tên riêng.\n" +
            "- Dùng giọng lịch sự, tự nhiên như người bản xứ nhắn tin công việc.\n" +
            "- Dịch theo ý, không dịch từng chữ.\n" +
            "- Không thêm giải thích hay ghi chú.";

        /// <summary>Mẫu bổ sung cho ngành thiết kế - thi công điện</summary>
        public const string ElectricalDesignPreset =
            "Lĩnh vực: thiết kế, cung cấp và thi công hệ thống điện công trình (tủ điện, cáp, chiếu sáng, chống sét, tiếp địa).\n" +
            "Giữ nguyên ký hiệu kỹ thuật, mã thiết bị, tiêu chuẩn, đơn vị: MCCB, ACB, MCB, RCBO, ATS, CT, kA, kVA, mm², IP54, IEC, TCVN.\n" +
            "Dùng đúng thuật ngữ sau:\n" +
            "- tủ điện tổng = Main Switchboard (MSB) / 总配电柜\n" +
            "- tủ phân phối = Distribution Board (DB) / 配电箱\n" +
            "- sơ đồ nguyên lý một sợi = Single Line Diagram (SLD) / 单线图\n" +
            "- bản vẽ thi công = Shop Drawing / 施工图\n" +
            "- bảng khối lượng = Bill of Quantities (BOQ) / 工程量清单\n" +
            "- máng cáp = cable trunking; thang cáp = cable ladder / 电缆桥架\n" +
            "- tiếp địa = earthing system / 接地系统; chống sét = lightning protection / 防雷\n" +
            "Khi dịch sang tiếng Việt: xưng \"em\", gọi khách là \"anh/chị\", giọng lịch sự, ngắn gọn.";

        /// <summary>
        /// Ghép prompt của người dùng với phần cố định (hướng dịch + định dạng JSON).
        /// Phần cố định luôn được thêm nên người dùng sửa/xóa prompt cũng không làm hỏng chức năng.
        /// </summary>
        private static string BuildInstruction(string src, string dst, string? userPrompt)
        {
            string prompt = string.IsNullOrWhiteSpace(userPrompt) ? DefaultPrompt : userPrompt.Trim();
            string from = src == AutoDetect ? "the source language (detect it automatically)" : EnglishName(src);
            return prompt + "\n\n" +
                "---\n" +
                $"Translate the user's message from {from} to {EnglishName(dst)}.\n" +
                "Reply ONLY with a JSON object: {\"translation\": \"<translated text>\", \"detected_language\": \"<source language code, " +
                "ISO 639-1, use zh-CN or zh-TW for Chinese>\"}. Do not wrap it in markdown.";
        }

        private static TranslationResult ParseAiJson(string content)
        {
            content = content.Trim();
            // Một số mô hình bọc JSON trong ```json ... ```
            if (content.StartsWith("```"))
            {
                int first = content.IndexOf('{');
                int last = content.LastIndexOf('}');
                if (first >= 0 && last > first)
                    content = content.Substring(first, last - first + 1);
            }
            string translation;
            string? detected = null;
            if (content.StartsWith("{"))
            {
                var node = JsonNode.Parse(content)!;
                translation = node["translation"]?.GetValue<string>() ?? string.Empty;
                detected = node["detected_language"]?.GetValue<string>();
            }
            else
            {
                // Dịch vụ không trả JSON (một số cổng tương thích OpenAI) -> coi cả nội dung là bản dịch
                translation = content;
            }
            if (string.IsNullOrWhiteSpace(translation))
                return TranslationResult.Fail("Dịch vụ không trả về bản dịch. Thử lại.", true);
            return new TranslationResult { Success = true, Text = translation.Trim(), DetectedLanguage = detected };
        }

        private static TranslationResult MapAiHttpError(string providerName, HttpStatusCode status, string body)
        {
            int code = (int)status;
            string lower = body.ToLowerInvariant();
            if (code == 401 || code == 403 || (code == 400 && (lower.Contains("api key") || lower.Contains("api_key"))))
                return TranslationResult.Fail($"Khóa API {providerName} không đúng hoặc chưa được cấp quyền. Kiểm tra lại trong Cài đặt → Dịch.", true);
            if (code == 402 || lower.Contains("insufficient balance"))
                return TranslationResult.Fail($"Tài khoản {providerName} đã hết tiền. Nạp thêm hoặc dùng Google Dịch.", true);
            if (code == 429)
                return TranslationResult.Fail($"{providerName} đã hết hạn mức (quá nhiều yêu cầu). Thử lại sau hoặc dùng Google Dịch.", true);
            if (code == 404)
                return TranslationResult.Fail($"Không tìm thấy mô hình của {providerName}. Kiểm tra tên mô hình trong Cài đặt → Dịch.", true);
            return TranslationResult.Fail($"{providerName} báo lỗi {code}.", true);
        }

        // ---------------- AI theo API URL ----------------
        private static async Task<TranslationResult> TranslateAiAsync(string text, string src, string dst,
            string apiUrl, string apiKey, string model, string? prompt, CancellationToken ct)
        {
            apiUrl = (apiUrl ?? string.Empty).Trim();
            if (apiUrl.Length == 0)
                return TranslationResult.Fail("Chưa nhập API URL. Vào Cài đặt → Dịch để nhập.", true);
            bool localServer = apiUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) || apiUrl.Contains("127.0.0.1");
            if (string.IsNullOrWhiteSpace(apiKey) && !localServer)
                return TranslationResult.Fail("Chưa nhập API key. Vào Cài đặt → Dịch để nhập.", true);

            return IsGeminiNativeUrl(apiUrl)
                ? await CallGeminiNativeAsync(text, src, dst, apiUrl, apiKey, model, prompt, ct)
                : await CallOpenAiCompatibleAsync(text, src, dst, apiUrl, apiKey, model, prompt, ct);
        }

        /// <summary>Cổng riêng của Gemini: …/models/{model}:generateContent (mô hình lấy từ URL)</summary>
        private static async Task<TranslationResult> CallGeminiNativeAsync(string text, string src, string dst,
            string apiUrl, string apiKey, string model, string? prompt, CancellationToken ct)
        {
            string url = apiUrl;
            if (!url.Contains(":generateContent", StringComparison.OrdinalIgnoreCase))
            {
                // Chỉ nhập gốc (vd …/v1beta) -> ghép thêm mô hình
                string m = string.IsNullOrWhiteSpace(model) ? DefaultGeminiModel : model.Trim();
                url = url.TrimEnd('/') + (url.Contains("/models", StringComparison.OrdinalIgnoreCase) ? "/" : "/models/") + m + ":generateContent";
            }

            var body = new JsonObject
            {
                ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = BuildInstruction(src, dst, prompt) }) },
                ["contents"] = new JsonArray(new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = text })
                }),
                ["generationConfig"] = new JsonObject
                {
                    ["temperature"] = 0.2,
                    ["responseMimeType"] = "application/json"
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            if (!url.Contains("key=", StringComparison.OrdinalIgnoreCase))
                req.Headers.Add("x-goog-api-key", apiKey.Trim());
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, ct);
            string json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return MapAiHttpError("Gemini", resp.StatusCode, json);

            var root = JsonNode.Parse(json)!;
            var parts = root["candidates"]?[0]?["content"]?["parts"]?.AsArray();
            if (parts == null)
            {
                string? reason = root["promptFeedback"]?["blockReason"]?.GetValue<string>();
                return TranslationResult.Fail(reason != null
                    ? $"Gemini từ chối dịch nội dung này ({reason})."
                    : "Gemini không trả về bản dịch.", true);
            }
            // Bỏ phần "suy nghĩ" (thought) nếu mô hình trả về
            string content = string.Concat(parts
                .Where(p => p?["thought"]?.GetValue<bool>() != true)
                .Select(p => p?["text"]?.GetValue<string>() ?? string.Empty));
            return ParseAiJson(content);
        }

        /// <summary>Chuẩn tương thích OpenAI: DeepSeek, OpenAI, OpenRouter, Ollama, Qwen…</summary>
        private static async Task<TranslationResult> CallOpenAiCompatibleAsync(string text, string src, string dst,
            string apiUrl, string apiKey, string model, string? prompt, CancellationToken ct)
        {
            string name = ServiceNameFromUrl(apiUrl);
            if (string.IsNullOrWhiteSpace(model))
                return TranslationResult.Fail($"Chưa nhập tên mô hình cho {name}. Vào Cài đặt → Dịch để nhập.", true);

            string url = apiUrl.TrimEnd('/');
            if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                url += "/chat/completions";

            async Task<(HttpResponseMessage resp, string json)> SendAsync(bool jsonMode)
            {
                var body = new JsonObject
                {
                    ["model"] = model.Trim(),
                    ["temperature"] = 0.2,
                    ["messages"] = new JsonArray(
                        new JsonObject { ["role"] = "system", ["content"] = BuildInstruction(src, dst, prompt) },
                        new JsonObject { ["role"] = "user", ["content"] = text })
                };
                if (jsonMode)
                    body["response_format"] = new JsonObject { ["type"] = "json_object" };

                var req = new HttpRequestMessage(HttpMethod.Post, url);
                if (!string.IsNullOrWhiteSpace(apiKey))
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());
                req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                var resp = await Http.SendAsync(req, ct);
                return (resp, await resp.Content.ReadAsStringAsync(ct));
            }

            var (response, responseJson) = await SendAsync(jsonMode: true);
            // Một số dịch vụ không hỗ trợ response_format -> gửi lại không kèm tùy chọn này
            if ((int)response.StatusCode == 400 && responseJson.Contains("response_format", StringComparison.OrdinalIgnoreCase))
            {
                response.Dispose();
                (response, responseJson) = await SendAsync(jsonMode: false);
            }
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    return MapAiHttpError(name, response.StatusCode, responseJson);
                string content = JsonNode.Parse(responseJson)!["choices"]![0]!["message"]!["content"]!.GetValue<string>();
                return ParseAiJson(content);
            }
        }
    }
}
