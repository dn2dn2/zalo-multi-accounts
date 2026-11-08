using System;
using System.Collections.Generic;
using ZaloMultiWorkspace.Models;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;

namespace ZaloMultiWorkspace.Services
{
    /// <summary>
    /// Bộ chọn DOM cho một nền tảng chat. Đọc từ selectors.json (mục "Zalo" / "Telegram")
    /// để sửa được khi trang web đổi giao diện mà không cần build lại.
    /// </summary>
    public class PlatformSelectors
    {
        public string ChatInputSelector { get; set; } = string.Empty;
        public string SendButtonSelector { get; set; } = string.Empty;
        public string AvatarSelector { get; set; } = string.Empty;
        public string AvatarBackgroundSelector { get; set; } = string.Empty;
        public string ActiveChatTitleSelector { get; set; } = string.Empty;
        /// <summary>Badge tổng trên thanh điều hướng (Zalo); để trống nếu nền tảng không có</summary>
        public string UnreadBadgeSelector { get; set; } = string.Empty;
        /// <summary>Chỉ nhận badge điều hướng nằm trong khoảng x &lt; giá trị này (0 = không giới hạn)</summary>
        public int NavBadgeMaxLeft { get; set; }
        public string ConversationUnreadBadgeSelector { get; set; } = string.Empty;
        public string ConversationItemSelector { get; set; } = string.Empty;
        public string ConversationItemBadgeSelector { get; set; } = string.Empty;
        public string ConversationNameSelector { get; set; } = string.Empty;
        public string ConversationPreviewSelector { get; set; } = string.Empty;
        /// <summary>Chỉ tính badge có số (Telegram có badge ghim không có số). false = badge không số tính là 1 (chấm đỏ Zalo)</summary>
        public bool ConversationBadgeRequiresNumber { get; set; }
        /// <summary>Cuộc chat chứa phần tử này là đã tắt tiếng -> không tính tin chưa đọc (giống bộ đếm của Telegram)</summary>
        public string ConversationMutedSelector { get; set; } = string.Empty;

        public static PlatformSelectors DefaultZalo() => new()
        {
            ChatInputSelector = "#richInput, div[contenteditable='true'][id*='input'], div[contenteditable='true']",
            SendButtonSelector = "[data-translate-title='STR_SEND'], .send-msg-btn, div[title='Gửi'], i.fa-Sent-msg_24_Line",
            AvatarSelector = "#main-tab img, nav img, [data-id='div_Main_Tab'] img, .zavatar img, .rel.zavatar-container img",
            AvatarBackgroundSelector = "#main-tab .zavatar, nav .zavatar, .zavatar",
            ActiveChatTitleSelector = "header .header-title, .thread-chat-title, #header .title",
            UnreadBadgeSelector = "#main-tab .z-noti-badge, nav .z-noti-badge, [class*='noti-badge'], [class*='unread-count'], .fa-num5",
            NavBadgeMaxLeft = 90,
            ConversationUnreadBadgeSelector = ".conv-item .z-noti-badge, .conv-item [class*='noti-badge'], .conv-item .fa-num5",
            ConversationItemSelector = ".conv-item, [id*='friend-item'], .msg-item",
            ConversationItemBadgeSelector = ".z-noti-badge, [class*='noti-badge'], .fa-num5",
            ConversationNameSelector = ".conv-item-title__name, .truncate, [class*='title']",
            ConversationPreviewSelector = ".conv-item-body__main, .conv-message, [class*='message']"
        };

        /// <summary>Telegram Web A (web.telegram.org/a), kèm bộ chọn dự phòng cho bản K</summary>
        public static PlatformSelectors DefaultTelegram() => new()
        {
            ChatInputSelector = "#editable-message-text, .input-message-input[contenteditable='true'], div[contenteditable='true'].form-control",
            SendButtonSelector = ".Button.send, button.send, .btn-send",
            ActiveChatTitleSelector = ".MiddleHeader .ChatInfo .fullName, .MiddleHeader .title h3, .chat-info .peer-title",
            NavBadgeMaxLeft = 0,
            // Telegram Web A dùng tên lớp mã hóa (vd UCrLRpZc), chỉ "chat-badge-transition" là ổn định.
            // Badge ghim cũng nằm trong lớp này nhưng không có số -> ConversationBadgeRequiresNumber = true
            ConversationUnreadBadgeSelector = ".ListItem.Chat .chat-badge-transition, .chatlist-chat .dialog-subtitle-badge-unread",
            ConversationItemSelector = ".ListItem.Chat, .chatlist-chat",
            ConversationItemBadgeSelector = ".chat-badge-transition, .dialog-subtitle-badge-unread",
            ConversationBadgeRequiresNumber = true,
            ConversationMutedSelector = ".icon-muted",
            ConversationNameSelector = ".fullName, .title h3, .peer-title",
            ConversationPreviewSelector = ".last-message, .subtitle, .dialog-subtitle"
        };
    }

    public class JsBridgeMessage
    {
        public string? Type { get; set; }
        public int Count { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Shortcut { get; set; }
        public string? ActiveChatName { get; set; }
        public string? Sender { get; set; }
        public string? Body { get; set; }
        public string? Text { get; set; }
    }

    public class JsBridgeService
    {
        private readonly Dictionary<ChatPlatform, PlatformSelectors> _selectors = new()
        {
            [ChatPlatform.Zalo] = PlatformSelectors.DefaultZalo(),
            [ChatPlatform.Telegram] = PlatformSelectors.DefaultTelegram()
        };

        public PlatformSelectors GetSelectors(ChatPlatform platform) =>
            _selectors.TryGetValue(platform, out var sel) ? sel : _selectors[ChatPlatform.Zalo];

        public JsBridgeService()
        {
            LoadSelectors();
        }

        public void LoadSelectors()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selectors.json");
                if (!File.Exists(path))
                    return;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;

                // Định dạng cũ (chỉ Zalo, khóa nằm ngay ở gốc)
                if (root.TryGetProperty("ChatInputSelector", out _))
                {
                    var legacy = root.Deserialize<PlatformSelectors>();
                    if (legacy != null)
                        _selectors[ChatPlatform.Zalo] = MergeWithDefaults(legacy, PlatformSelectors.DefaultZalo());
                    return;
                }

                foreach (ChatPlatform platform in Enum.GetValues(typeof(ChatPlatform)))
                {
                    if (root.TryGetProperty(platform.ToString(), out var section))
                    {
                        var loaded = section.Deserialize<PlatformSelectors>();
                        if (loaded != null)
                            _selectors[platform] = MergeWithDefaults(loaded, GetSelectors(platform));
                    }
                }
            }
            catch
            {
                // Giữ selector mặc định nếu file JSON lỗi
            }
        }

        /// <summary>Trường nào trong file bị thiếu thì dùng giá trị mặc định</summary>
        private static PlatformSelectors MergeWithDefaults(PlatformSelectors loaded, PlatformSelectors defaults)
        {
            // Trường bool mới: file selectors.json cũ không có -> lấy theo mặc định của nền tảng
            if (!loaded.ConversationBadgeRequiresNumber && defaults.ConversationBadgeRequiresNumber)
                loaded.ConversationBadgeRequiresNumber = true;
            foreach (var prop in typeof(PlatformSelectors).GetProperties())
            {
                if (prop.PropertyType == typeof(string) && string.IsNullOrWhiteSpace((string?)prop.GetValue(loaded)))
                {
                    // Chuỗi rỗng có chủ đích (vd Telegram không có avatar) vẫn hợp lệ nếu mặc định cũng rỗng
                    prop.SetValue(loaded, prop.GetValue(defaults));
                }
            }
            return loaded;
        }

        /// <summary>Phím tắt Dịch đoạn đã chọn trong trang chat (cập nhật từ Cài đặt)</summary>
        public HotkeyGesture TranslateHotkey { get; set; } = HotkeyGesture.Parse("Ctrl+Shift+D");

        /// <summary>Đổi phím tắt Dịch cho trang đang mở (trang mở sau sẽ lấy từ script khởi tạo)</summary>
        public async Task ApplyTranslateHotkeyAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null)
                return;
            try { await webView.CoreWebView2.ExecuteScriptAsync($"window.__cwpTranslateKey = {TranslateHotkey.ToJsObject()};"); }
            catch { }
        }

        public string BuildStartupObserverScript(ChatPlatform platform)
        {
            var sel = GetSelectors(platform);
            string J(string v) => JsonSerializer.Serialize(v ?? string.Empty);
            string chatSelectorJson = J(sel.ChatInputSelector);
            string avatarSelectorJson = J(sel.AvatarSelector);
            string activeChatSelectorJson = J(sel.ActiveChatTitleSelector);
            string unreadBadgeSelectorJson = J(sel.UnreadBadgeSelector);

            return $@"
(function() {{
    window.__cwpTranslateKey = {TranslateHotkey.ToJsObject()};
    const CHAT_INPUT_SEL = {chatSelectorJson};
    const AVATAR_SEL = {avatarSelectorJson};
    const ACTIVE_CHAT_SEL = {activeChatSelectorJson};
    const UNREAD_BADGE_SEL = {unreadBadgeSelectorJson};
    const AVATAR_BG_SEL = {J(sel.AvatarBackgroundSelector)};
    const NAV_BADGE_MAX_LEFT = {sel.NavBadgeMaxLeft};
    const CONV_BADGE_SEL = {J(sel.ConversationUnreadBadgeSelector)};
    const CONV_ITEM_SEL = {J(sel.ConversationItemSelector)};
    const CONV_ITEM_BADGE_SEL = {J(sel.ConversationItemBadgeSelector)};
    const CONV_NAME_SEL = {J(sel.ConversationNameSelector)};
    const CONV_MSG_SEL = {J(sel.ConversationPreviewSelector)};
    const CONV_REQUIRE_NUMBER = {(sel.ConversationBadgeRequiresNumber ? "true" : "false")};
    const CONV_MUTED_SEL = {J(sel.ConversationMutedSelector)};

    // Số tin trên một badge hội thoại: số trên badge; badge không có số tính 1 (chấm đỏ Zalo) hoặc 0 (badge ghim Telegram)
    function badgeCount(b) {{
        const txt = (b.innerText || b.textContent || '').replace('+', '').trim();
        const num = parseInt(txt, 10);
        if (!isNaN(num) && num > 0 && /^\d+$/.test(txt)) return num;
        return CONV_REQUIRE_NUMBER ? 0 : 1;
    }}
    function isMutedItem(el) {{
        if (!CONV_MUTED_SEL || !CONV_ITEM_SEL) return false;
        let item = null;
        try {{ item = el.closest(CONV_ITEM_SEL); }} catch (e) {{}}
        return !!(item && qs(item, CONV_MUTED_SEL));
    }}

    // querySelectorAll an toàn: bộ chọn rỗng hoặc sai cú pháp trả về danh sách rỗng
    function qsa(root, sel) {{
        if (!sel) return [];
        try {{ return root.querySelectorAll(sel); }} catch (e) {{ return []; }}
    }}
    function qs(root, sel) {{
        if (!sel) return null;
        try {{ return root.querySelector(sel); }} catch (e) {{ return null; }}
    }}

    let lastUnreadSent = -1;

    // 1. Hook window.Notification để bắt ngay thông báo tin nhắn mới từ Zalo Web
    try {{
        const OriginalNotification = window.Notification;
        function CustomNotification(title, options) {{
            if (window.chrome && window.chrome.webview) {{
                window.chrome.webview.postMessage({{
                    type: 'NEW_MESSAGE_NOTIFY',
                    sender: title || 'Tin nhắn Zalo mới',
                    body: (options && options.body) ? options.body : 'Bạn có tin nhắn mới'
                }});
            }}
            if (OriginalNotification) {{
                try {{ return new OriginalNotification(title, options); }} catch (e) {{}}
            }}
        }}
        CustomNotification.permission = 'granted';
        CustomNotification.requestPermission = function(cb) {{
            if (typeof cb === 'function') cb('granted');
            return Promise.resolve('granted');
        }};
        window.Notification = CustomNotification;
        // Một số trang (Telegram Web) hiện thông báo qua service worker -> bắt cả cách này
        if (window.ServiceWorkerRegistration && ServiceWorkerRegistration.prototype.showNotification) {{
            const origShow = ServiceWorkerRegistration.prototype.showNotification;
            ServiceWorkerRegistration.prototype.showNotification = function(title, options) {{
                try {{
                    if (window.chrome && window.chrome.webview) {{
                        window.chrome.webview.postMessage({{
                            type: 'NEW_MESSAGE_NOTIFY',
                            sender: title || 'Tin nhắn mới',
                            body: (options && options.body) ? options.body : 'Bạn có tin nhắn mới'
                        }});
                    }}
                }} catch (e) {{}}
                return origShow.apply(this, arguments);
            }};
        }}
    }} catch (e) {{}}

    // 2. Tính tổng số tin nhắn chưa đọc từ cả <title> và DOM badge hiển thị thực tế của Zalo Web
    function isBadgeVisible(el) {{
        if (!el || !el.isConnected) return false;
        const style = window.getComputedStyle(el);
        if (!style || style.display === 'none' || style.visibility === 'hidden' || parseFloat(style.opacity) === 0) {{
            return false;
        }}
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    }}

    function computeUnreadCount() {{
        let countFromTitle = 0;
        if (document.title) {{
            const match = document.title.match(/\((\d+)\)/);
            if (match) {{
                countFromTitle = parseInt(match[1], 10) || 0;
            }}
        }}

        let countFromDom = 0;
        try {{
            // Kiểm tra badge trên biểu tượng Tin nhắn ở thanh điều hướng bên trái (#main-tab)
            const navBadges = qsa(document, UNREAD_BADGE_SEL);
            for (const b of navBadges) {{
                if (!isBadgeVisible(b)) continue;
                const rect = b.getBoundingClientRect();
                const txt = (b.innerText || b.textContent || '').replace('+', '').trim();
                const num = parseInt(txt, 10);
                if (!isNaN(num) && num > 0) {{
                    if (NAV_BADGE_MAX_LEFT <= 0 || (rect.left >= 0 && rect.left < NAV_BADGE_MAX_LEFT && rect.top >= 40 && rect.top < 170)) {{
                        countFromDom = Math.max(countFromDom, num);
                    }}
                }}
            }}

            // Nếu không tìm thấy số trên thanh trái, cộng các badge số hoặc chấm đỏ đang hiển thị trong danh sách hội thoại
            if (countFromDom === 0) {{
                let sumConv = 0;
                const convBadges = qsa(document, CONV_BADGE_SEL);
                for (const b of convBadges) {{
                    if (!isBadgeVisible(b) || isMutedItem(b)) continue;
                    sumConv += badgeCount(b);
                }}
                if (sumConv > 0) countFromDom = sumConv;
            }}
        }} catch (e) {{}}

        return Math.max(countFromTitle, countFromDom);
    }}

    function extractLatestUnreadPreview() {{
        try {{
            const convItems = qsa(document, CONV_ITEM_SEL);
            for (const item of convItems) {{
                const badge = Array.from(qsa(item, CONV_ITEM_BADGE_SEL)).find(b => isBadgeVisible(b) && badgeCount(b) > 0);
                if (badge && !isMutedItem(badge)) {{
                    const nameEl = qs(item, CONV_NAME_SEL);
                    const msgEl = qs(item, CONV_MSG_SEL);
                    const sender = nameEl ? (nameEl.innerText || '').trim() : '';
                    const body = msgEl ? (msgEl.innerText || '').trim() : '';
                    if (sender || body) {{
                        return {{ sender: sender || 'Khách hàng', body: body || 'Tin nhắn mới' }};
                    }}
                }}
            }}
        }} catch (e) {{}}
        return null;
    }}

    function checkAndSendUnread(forceSync = false) {{
        const currentUnread = computeUnreadCount();
        if (forceSync || currentUnread !== lastUnreadSent) {{
            const increased = (lastUnreadSent >= 0 && currentUnread > lastUnreadSent);
            lastUnreadSent = currentUnread;
            if (window.chrome && window.chrome.webview) {{
                const preview = increased ? extractLatestUnreadPreview() : null;
                window.chrome.webview.postMessage({{
                    type: 'UNREAD_UPDATE',
                    count: currentUnread,
                    sender: preview ? preview.sender : '',
                    body: preview ? preview.body : ''
                }});
            }}
        }}
    }}

    function attachTitleObserver() {{
        const titleEl = document.querySelector('title');
        if (titleEl) {{
            const observer = new MutationObserver(checkAndSendUnread);
            observer.observe(titleEl, {{ childList: true, characterData: true, subtree: true }});
        }}
        checkAndSendUnread();
    }}

    let lastAvatarSent = '';
    let lastActiveChatSent = '';

    function findMyAvatarUrl() {{
        const imgs = qsa(document, AVATAR_SEL);
        for (const img of imgs) {{
            const rect = img.getBoundingClientRect();
            if (rect.width >= 24 && rect.left >= 0 && rect.left < 90 && rect.top >= 0 && rect.top < 180) {{
                if (img.src && (img.src.startsWith('http') || img.src.startsWith('data:image'))) {{
                    return img.src;
                }}
            }}
        }}
        const bgEls = qsa(document, AVATAR_BG_SEL);
        for (const el of bgEls) {{
            const rect = el.getBoundingClientRect();
            if (rect.width >= 24 && rect.left >= 0 && rect.left < 90 && rect.top >= 0 && rect.top < 180) {{
                const bg = window.getComputedStyle(el).backgroundImage;
                if (bg && bg !== 'none') {{
                    const m = bg.match(/url\([""']?(.*?)[""']?\)/);
                    if (m && m[1]) return m[1];
                }}
            }}
        }}
        return null;
    }}

    function pollProfileAndChatInfo() {{
        try {{
            const rawUrl = findMyAvatarUrl();
            if (rawUrl && rawUrl !== lastAvatarSent) {{
                lastAvatarSent = rawUrl;
                if (rawUrl.startsWith('data:image')) {{
                    window.chrome.webview.postMessage({{ type: 'AVATAR_UPDATE', avatarUrl: rawUrl }});
                }} else {{
                    // Thử chuyển đổi sang PNG Base64 qua Canvas để hỗ trợ cả định dạng WebP
                    const tempImg = new Image();
                    tempImg.crossOrigin = 'anonymous';
                    tempImg.onload = function() {{
                        try {{
                            const canvas = document.createElement('canvas');
                            canvas.width = 64;
                            canvas.height = 64;
                            const ctx = canvas.getContext('2d');
                            ctx.drawImage(tempImg, 0, 0, 64, 64);
                            const dataUri = canvas.toDataURL('image/png');
                            window.chrome.webview.postMessage({{ type: 'AVATAR_UPDATE', avatarUrl: dataUri }});
                        }} catch (err) {{
                            window.chrome.webview.postMessage({{ type: 'AVATAR_UPDATE', avatarUrl: rawUrl }});
                        }}
                    }};
                    tempImg.onerror = function() {{
                        window.chrome.webview.postMessage({{ type: 'AVATAR_UPDATE', avatarUrl: rawUrl }});
                    }};
                    tempImg.src = rawUrl;
                }}
            }}
            const chatEl = qs(document, ACTIVE_CHAT_SEL);
            const chatName = (chatEl && chatEl.innerText) ? chatEl.innerText.trim() : '';
            // Chỉ gửi khi người dùng chuyển sang cuộc hội thoại khác
            if (chatName && chatName !== lastActiveChatSent) {{
                lastActiveChatSent = chatName;
                window.chrome.webview.postMessage({{ type: 'ACTIVE_CHAT', activeChatName: chatName }});
            }}
        }} catch (e) {{}}
    }}

    // Bắt tương tác người dùng để cập nhật bộ đếm ngủ đông
    let lastPing = 0;
    const notifyActivity = () => {{
        const now = Date.now();
        if (now - lastPing > 5000 && window.chrome && window.chrome.webview) {{
            lastPing = now;
            window.chrome.webview.postMessage({{ type: 'USER_ACTIVITY' }});
        }}
    }};
    window.addEventListener('mousemove', notifyActivity, {{ passive: true }});
    window.addEventListener('click', () => {{
        notifyActivity();
        setTimeout(() => checkAndSendUnread(true), 250);
        setTimeout(() => checkAndSendUnread(true), 800);
    }}, true);
    window.addEventListener('focus', () => {{
        setTimeout(() => checkAndSendUnread(true), 300);
    }}, true);
    window.addEventListener('keydown', (e) => {{
        notifyActivity();
        // Phím tắt Dịch (mặc định Ctrl+Shift+D, đổi được trong Cài đặt): gửi đoạn đang bôi đen sang tab Dịch
        const tk = window.__cwpTranslateKey;
        if (tk && e.code === tk.code && e.ctrlKey === tk.ctrl && e.altKey === tk.alt && e.shiftKey === tk.shift) {{
            const sel = (window.getSelection ? window.getSelection().toString() : '').trim();
            if (sel && window.chrome && window.chrome.webview) {{
                window.chrome.webview.postMessage({{ type: 'TRANSLATE_SELECTION', text: sel }});
            }}
            e.preventDefault();
            e.stopPropagation();
            return;
        }}
        // Bắt phím Space hoặc Tab sau khi gõ cú pháp tắt /shortcut trong ô chat
        if (e.key === ' ' || e.key === 'Tab') {{
            const inputEl = qs(document, CHAT_INPUT_SEL);
            if (inputEl && document.activeElement === inputEl) {{
                const text = (inputEl.innerText || inputEl.textContent || '').trim();
                if (text.startsWith('/') && text.length > 1 && !text.includes(' ')) {{
                    window.chrome.webview.postMessage({{ type: 'SHORTCUT_TRIGGER', shortcut: text }});
                }}
            }}
        }}
    }}, true);

    if (document.readyState === 'loading') {{
        document.addEventListener('DOMContentLoaded', attachTitleObserver);
    }} else {{
        attachTitleObserver();
    }}
    window.checkZaloUnreadNow = () => checkAndSendUnread(true);


    setInterval(() => {{
        checkAndSendUnread(false);
        pollProfileAndChatInfo();
    }}, 1500);
}})();";
        }

        /// <summary>Lấy đoạn chữ đang bôi đen trong trang chat</summary>
        public async Task<string> GetSelectedTextAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null)
                return string.Empty;
            try
            {
                string json = await webView.CoreWebView2.ExecuteScriptAsync(
                    "(window.getSelection ? window.getSelection().toString() : '')");
                return (JsonSerializer.Deserialize<string>(json) ?? string.Empty).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static void SimulateCtrlV()
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        /// <summary>Dán ảnh (vd ảnh chụp màn hình) vào ô chat qua Clipboard + Ctrl+V. Người dùng tự bấm Gửi.</summary>
        public async Task<bool> PasteImageAsync(WebView2 webView, ChatPlatform platform, BitmapSource image)
        {
            if (webView?.CoreWebView2 == null)
                return false;
            if (!Views.ScreenCaptureWindow.SetClipboardImage(image))
                return false;
            webView.Focus();
            string focusScript = $@"
(function() {{
    const el = document.querySelector({JsonSerializer.Serialize(GetSelectors(platform).ChatInputSelector)});
    if (el) {{ el.focus(); return true; }}
    return false;
}})();";
            string found = await webView.CoreWebView2.ExecuteScriptAsync(focusScript);
            await Task.Delay(120);
            SimulateCtrlV();
            await Task.Delay(200);
            return found == "true";
        }

        public async Task InsertQuickReplyAsync(WebView2 webView, ChatPlatform platform, string text, string? imagePath, bool sendImmediately, bool replaceAll = false)
        {
            if (webView?.CoreWebView2 == null)
                return;

            // 1. Nếu có hình ảnh đính kèm: đưa vào Clipboard và Paste vào khung chat Zalo
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bitmap.EndInit();
                    Clipboard.SetImage(bitmap);

                    webView.Focus();
                    string focusScript = $@"
(function() {{
    const el = document.querySelector({JsonSerializer.Serialize(GetSelectors(platform).ChatInputSelector)});
    if (el) el.focus();
}})();";
                    await webView.CoreWebView2.ExecuteScriptAsync(focusScript);
                    await Task.Delay(80);
                    SimulateCtrlV();
                    await Task.Delay(250);
                }
                catch
                {
                    // Bỏ qua nếu ảnh không hợp lệ
                }
            }

            // 2. Bơm văn bản vào #richInput của Zalo Web
            string escapedText = JsonSerializer.Serialize(text);
            string chatInputSel = JsonSerializer.Serialize(GetSelectors(platform).ChatInputSelector);
            string sendBtnSel = JsonSerializer.Serialize(GetSelectors(platform).SendButtonSelector);
            string replaceFlag = replaceAll ? "true" : "false";
            string sendFlag = sendImmediately ? "true" : "false";

            string script = $@"
(function() {{
    const el = document.querySelector({chatInputSel});
    if (!el) return false;
    el.focus();
    if ({replaceFlag}) {{
        document.execCommand('selectAll', false, null);
        document.execCommand('delete', false, null);
    }}
    const inserted = document.execCommand('insertText', false, {escapedText});
    if (!inserted) {{
        el.innerText = {replaceFlag} ? {escapedText} : (el.innerText + {escapedText});
        el.dispatchEvent(new InputEvent('input', {{ bubbles: true, cancelable: true }}));
    }}
    if ({sendFlag}) {{
        setTimeout(() => {{
            const sendBtn = document.querySelector({sendBtnSel});
            if (sendBtn) {{
                sendBtn.click();
            }} else {{
                const enterEvent = new KeyboardEvent('keydown', {{
                    key: 'Enter',
                    code: 'Enter',
                    keyCode: 13,
                    which: 13,
                    bubbles: true,
                    cancelable: true
                }});
                el.dispatchEvent(enterEvent);
            }}
        }}, 120);
    }}
    return true;
}})();";

            webView.Focus();
            await webView.CoreWebView2.ExecuteScriptAsync(script);
        }
    }
}
