using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;

namespace ZaloMultiWorkspace.Services
{
    public class ZaloDomSelectors
    {
        public string ChatInputSelector { get; set; } = "#richInput, div[contenteditable='true'][id*='input'], div[contenteditable='true']";
        public string SendButtonSelector { get; set; } = "[data-translate-title='STR_SEND'], .send-msg-btn, div[title='Gửi'], i.fa-Sent-msg_24_Line";
        public string AvatarSelector { get; set; } = "#main-tab img, nav img, [data-id='div_Main_Tab'] img, .zavatar img, .rel.zavatar-container img";
        public string ActiveChatTitleSelector { get; set; } = "header .header-title, .thread-chat-title, #header .title";
        public string UnreadBadgeSelector { get; set; } = "#main-tab .z-noti-badge, nav .z-noti-badge, [class*='noti-badge'], [class*='unread-count'], .fa-num5";
        public string TitleElementSelector { get; set; } = "title";
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
    }

    public class JsBridgeService
    {
        private ZaloDomSelectors _selectors = new();

        public JsBridgeService()
        {
            LoadSelectors();
        }

        public void LoadSelectors()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selectors.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<ZaloDomSelectors>(json);
                    if (loaded != null)
                        _selectors = loaded;
                }
            }
            catch
            {
                // Giữ selector mặc định nếu file JSON lỗi
            }
        }

        public string BuildStartupObserverScript()
        {
            string chatSelectorJson = JsonSerializer.Serialize(_selectors.ChatInputSelector);
            string avatarSelectorJson = JsonSerializer.Serialize(_selectors.AvatarSelector);
            string activeChatSelectorJson = JsonSerializer.Serialize(_selectors.ActiveChatTitleSelector);
            string unreadBadgeSelectorJson = JsonSerializer.Serialize(_selectors.UnreadBadgeSelector);

            return $@"
(function() {{
    const CHAT_INPUT_SEL = {chatSelectorJson};
    const AVATAR_SEL = {avatarSelectorJson};
    const ACTIVE_CHAT_SEL = {activeChatSelectorJson};
    const UNREAD_BADGE_SEL = {unreadBadgeSelectorJson};

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
            const navBadges = document.querySelectorAll(UNREAD_BADGE_SEL);
            for (const b of navBadges) {{
                if (!isBadgeVisible(b)) continue;
                const rect = b.getBoundingClientRect();
                const txt = (b.innerText || b.textContent || '').replace('+', '').trim();
                const num = parseInt(txt, 10);
                if (!isNaN(num) && num > 0) {{
                    if (rect.left >= 0 && rect.left < 90 && rect.top >= 40 && rect.top < 170) {{
                        countFromDom = Math.max(countFromDom, num);
                    }}
                }}
            }}

            // Nếu không tìm thấy số trên thanh trái, cộng các badge số hoặc chấm đỏ đang hiển thị trong danh sách hội thoại
            if (countFromDom === 0) {{
                let sumConv = 0;
                const convBadges = document.querySelectorAll('.conv-item .z-noti-badge, .conv-item [class*=""noti-badge""], .conv-item .fa-num5');
                for (const b of convBadges) {{
                    if (!isBadgeVisible(b)) continue;
                    const txt = (b.innerText || b.textContent || '').replace('+', '').trim();
                    const num = parseInt(txt, 10);
                    sumConv += (!isNaN(num) && num > 0) ? num : 1;
                }}
                if (sumConv > 0) countFromDom = sumConv;
            }}
        }} catch (e) {{}}

        return Math.max(countFromTitle, countFromDom);
    }}

    function extractLatestUnreadPreview() {{
        try {{
            const convItems = document.querySelectorAll('.conv-item, [id*=""friend-item""], .msg-item');
            for (const item of convItems) {{
                const badge = item.querySelector('.z-noti-badge, [class*=""noti-badge""], .fa-num5');
                if (badge && isBadgeVisible(badge)) {{
                    const nameEl = item.querySelector('.conv-item-title__name, .truncate, [class*=""title""]');
                    const msgEl = item.querySelector('.conv-item-body__main, .conv-message, [class*=""message""]');
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
        const imgs = document.querySelectorAll(AVATAR_SEL);
        for (const img of imgs) {{
            const rect = img.getBoundingClientRect();
            if (rect.width >= 24 && rect.left >= 0 && rect.left < 90 && rect.top >= 0 && rect.top < 180) {{
                if (img.src && (img.src.startsWith('http') || img.src.startsWith('data:image'))) {{
                    return img.src;
                }}
            }}
        }}
        const bgEls = document.querySelectorAll('#main-tab .zavatar, nav .zavatar, .zavatar');
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
            const chatEl = document.querySelector(ACTIVE_CHAT_SEL);
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
        // Bắt phím Space hoặc Tab sau khi gõ cú pháp tắt /shortcut trong ô chat
        if (e.key === ' ' || e.key === 'Tab') {{
            const inputEl = document.querySelector(CHAT_INPUT_SEL);
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

        public async Task InsertQuickReplyAsync(WebView2 webView, string text, string? imagePath, bool sendImmediately, bool replaceAll = false)
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
    const el = document.querySelector({JsonSerializer.Serialize(_selectors.ChatInputSelector)});
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
            string chatInputSel = JsonSerializer.Serialize(_selectors.ChatInputSelector);
            string sendBtnSel = JsonSerializer.Serialize(_selectors.SendButtonSelector);
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
