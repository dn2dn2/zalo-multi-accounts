using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using ZaloMultiWorkspace.Models;

namespace ZaloMultiWorkspace.Services
{
    public class DatabaseService
    {
        public static string BaseAppDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZaloMultiWorkspace"); // Giữ tên thư mục cũ để dùng tiếp dữ liệu (tài khoản, đăng nhập) đã có

        public static string BaseProfileDir => Path.Combine(BaseAppDir, "Profiles");

        public static string DbFilePath => Path.Combine(BaseAppDir, "ZaloWorkspace.db");

        public DatabaseService()
        {
            Directory.CreateDirectory(BaseAppDir);
            Directory.CreateDirectory(BaseProfileDir);
            EnsureSeedData();
            RunMigrations();
        }

        private AppSettings? _cachedSettings;

        /// <summary>
        /// Chuyển dữ liệu cũ sang định dạng an toàn: mã hóa mật khẩu Proxy (DPAPI) và băm mã PIN.
        /// Gọi lại sau khi khôi phục backup.
        /// </summary>
        public void RunMigrations()
        {
            _cachedSettings = null;
            using var db = OpenDb();

            var rawAccounts = db.GetCollection("accounts");
            foreach (var doc in rawAccounts.FindAll().ToList())
            {
                if (doc.TryGetValue("ProxyPass", out var legacy))
                {
                    string plain = legacy.IsString ? legacy.AsString : string.Empty;
                    if (!string.IsNullOrEmpty(plain) &&
                        (!doc.TryGetValue("ProxyPassProtected", out var prot) || string.IsNullOrEmpty(prot.AsString)))
                    {
                        doc["ProxyPassProtected"] = SecurityHelper.Protect(plain);
                    }
                    doc.Remove("ProxyPass");
                    rawAccounts.Update(doc);
                }
            }

            var settingsCol = db.GetCollection<AppSettings>("settings");
            var settings = settingsCol.FindById(1) ?? new AppSettings();

            // Một lần: tài khoản còn để mặc định cũ (tự tạm dừng sau 30 phút) chuyển sang không tự tạm dừng,
            // vì tài khoản tạm dừng không nhận được tin nhắn mới
            if (!settings.AutoHibernateDefaultMigrated)
            {
                foreach (var doc in rawAccounts.FindAll().ToList())
                {
                    if (doc.TryGetValue("AutoHibernateMinutes", out var mins) && mins.IsInt32 && mins.AsInt32 == 30)
                    {
                        doc["AutoHibernateMinutes"] = 0;
                        rawAccounts.Update(doc);
                    }
                }
                settings.AutoHibernateDefaultMigrated = true;
                settingsCol.Upsert(settings);
            }
            // Một lần: chuyển cấu hình dịch Gemini/DeepSeek riêng (bản trước) sang AI theo API URL
            if (!settings.AiConfigMigrated)
            {
                if (!string.IsNullOrEmpty(settings.GeminiApiKeyProtected))
                    settings.AiKeysProtected["Gemini"] = settings.GeminiApiKeyProtected;
                if (!string.IsNullOrEmpty(settings.DeepSeekApiKeyProtected))
                    settings.AiKeysProtected["DeepSeek"] = settings.DeepSeekApiKeyProtected;

                bool useGemini = settings.TranslateProvider == TranslationProvider.Gemini
                                 || (settings.TranslateProvider != TranslationProvider.DeepSeek
                                     && string.IsNullOrEmpty(settings.DeepSeekApiKeyProtected)
                                     && !string.IsNullOrEmpty(settings.GeminiApiKeyProtected));
                var preset = TranslationService.AiPresets.First(p => p.Id == (useGemini ? "Gemini" : "DeepSeek"));
                settings.AiPresetId = preset.Id;
                settings.AiApiUrl = preset.Url;
                settings.AiModel = useGemini ? string.Empty : settings.DeepSeekModel;
                // Mô hình Gemini đã tự đổi ở bản trước thì giữ lại trong URL
                if (useGemini && !string.IsNullOrWhiteSpace(settings.GeminiModel)
                    && settings.GeminiModel != TranslationService.DefaultGeminiModel)
                {
                    settings.AiApiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{settings.GeminiModel}:generateContent";
                }
                if (settings.TranslateProvider is TranslationProvider.Gemini or TranslationProvider.DeepSeek)
                    settings.TranslateProvider = TranslationProvider.Ai;
                settings.AiConfigMigrated = true;
                settingsCol.Upsert(settings);
            }

            if (string.IsNullOrEmpty(settings.MasterPinHash))
            {
                string pin = string.IsNullOrWhiteSpace(settings.MasterPin) ? AppSettings.DefaultPin : settings.MasterPin.Trim();
                var (hash, salt) = SecurityHelper.HashPin(pin);
                settings.MasterPinHash = hash;
                settings.MasterPinSalt = salt;
                settings.IsUsingDefaultPin = pin == AppSettings.DefaultPin;
                settings.MasterPin = string.Empty;
                settingsCol.Upsert(settings);
            }
            else if (!string.IsNullOrEmpty(settings.MasterPin))
            {
                settings.MasterPin = string.Empty;
                settingsCol.Upsert(settings);
            }
        }

        private LiteDatabase OpenDb()
        {
            return new LiteDatabase($"Filename={DbFilePath};Connection=shared");
        }

        private void EnsureSeedData()
        {
            using var db = OpenDb();
            var settingsCol = db.GetCollection<AppSettings>("settings");
            var existingSettings = settingsCol.FindById(1);

            // Nếu đã có bản ghi settings trên máy (đã qua lần mở đầu tiên), chỉ đánh dấu cờ IsInitialDataSeeded = true
            // để khi người dùng xóa trắng hết tin mẫu hoặc tài khoản về 0 thì lần mở sau KHÔNG tự nạp lại dữ liệu mẫu.
            if (existingSettings != null)
            {
                if (!existingSettings.IsInitialDataSeeded)
                {
                    existingSettings.IsInitialDataSeeded = true;
                    settingsCol.Upsert(existingSettings);
                }
                return;
            }

            var accounts = db.GetCollection<ZaloAccountProfile>("accounts");
            if (accounts.Count() == 0)
            {
                var acc1 = new ZaloAccountProfile
                {
                    Id = Guid.NewGuid(),
                    DisplayName = "Kỹ thuật 01",
                    TagCategory = "Báo giá & Dự án",
                    TagColor = "#1E3A8A",
                    ProxyType = "http",
                    ProxyAddress = "",
                    ProxyHost = "",
                    ProxyPort = 0,
                    AutoHibernateMinutes = 0,
                    SortOrder = 1
                };
                var acc2 = new ZaloAccountProfile
                {
                    Id = Guid.NewGuid(),
                    DisplayName = "Sale 02",
                    TagCategory = "CSKH",
                    TagColor = "#065F46",
                    ProxyType = "http",
                    ProxyAddress = "",
                    ProxyHost = "",
                    ProxyPort = 0,
                    AutoHibernateMinutes = 0,
                    SortOrder = 2
                };
                var acc3 = new ZaloAccountProfile
                {
                    Id = Guid.NewGuid(),
                    DisplayName = "Dự phòng",
                    TagCategory = "Tài khoản phụ",
                    TagColor = "#78350F",
                    ProxyType = "http",
                    ProxyAddress = "",
                    ProxyHost = "",
                    ProxyPort = 0,
                    AutoHibernateMinutes = 0,
                    SortOrder = 3
                };
                accounts.InsertBulk(new[] { acc1, acc2, acc3 });
            }

            var quickReplies = db.GetCollection<QuickReplyTemplate>("quick_replies");
            if (quickReplies.Count() == 0)
            {
                quickReplies.InsertBulk(new[]
                {
                    new QuickReplyTemplate
                    {
                        Id = Guid.NewGuid(),
                        Shortcut = "/chao",
                        Title = "Chào khách mới",
                        Content = "Dạ chào anh/chị ạ! Anh/chị cần tư vấn thông số kỹ thuật hay báo giá hạng mục nào cứ nhắn em hỗ trợ ngay nhé."
                    },
                    new QuickReplyTemplate
                    {
                        Id = Guid.NewGuid(),
                        Shortcut = "/baogia",
                        Title = "Gửi bảng báo giá tiêu chuẩn",
                        Content = "Em gửi anh/chị bảng báo giá chi tiết và chính sách chiết khấu mới nhất. Anh/chị xem qua nếu cần điều chỉnh số lượng báo em nhé ạ!"
                    },
                    new QuickReplyTemplate
                    {
                        Id = Guid.NewGuid(),
                        Shortcut = "/stk",
                        Title = "Thông tin thanh toán",
                        Content = "Dạ thông tin tài khoản thanh toán bên em:\n- Ngân hàng: Vietcombank\n- STK: 0123456789\n- Nội dung: [Tên KH + SĐT]"
                    }
                });
            }

            settingsCol.Upsert(new AppSettings { Id = 1, IsInitialDataSeeded = true });
        }

        // --- Accounts CRUD ---
        public List<ZaloAccountProfile> GetAccounts()
        {
            using var db = OpenDb();
            return db.GetCollection<ZaloAccountProfile>("accounts")
                     .FindAll()
                     .OrderBy(a => a.SortOrder)
                     .ToList();
        }

        public void UpsertAccount(ZaloAccountProfile profile)
        {
            using var db = OpenDb();
            db.GetCollection<ZaloAccountProfile>("accounts").Upsert(profile);
        }

        public void DeleteAccount(Guid id)
        {
            using var db = OpenDb();
            db.GetCollection<ZaloAccountProfile>("accounts").Delete(id);
        }

        // --- Quick Replies CRUD ---
        public List<QuickReplyTemplate> GetQuickReplies()
        {
            using var db = OpenDb();
            return db.GetCollection<QuickReplyTemplate>("quick_replies").FindAll().ToList();
        }

        public void UpsertQuickReply(QuickReplyTemplate template)
        {
            using var db = OpenDb();
            db.GetCollection<QuickReplyTemplate>("quick_replies").Upsert(template);
        }

        public void DeleteQuickReply(Guid id)
        {
            using var db = OpenDb();
            db.GetCollection<QuickReplyTemplate>("quick_replies").Delete(id);
        }

        // --- Customer Notes CRUD ---
        public List<CustomerNote> GetCustomerNotes(Guid? accountId = null)
        {
            using var db = OpenDb();
            var col = db.GetCollection<CustomerNote>("customer_notes");
            if (accountId.HasValue)
                return col.Find(n => n.AccountId == accountId.Value).OrderByDescending(n => n.UpdatedAt).ToList();
            return col.FindAll().OrderByDescending(n => n.UpdatedAt).ToList();
        }

        public void UpsertCustomerNote(CustomerNote note)
        {
            note.UpdatedAt = DateTime.Now;
            using var db = OpenDb();
            db.GetCollection<CustomerNote>("customer_notes").Upsert(note);
        }

        public void DeleteCustomerNote(Guid id)
        {
            using var db = OpenDb();
            db.GetCollection<CustomerNote>("customer_notes").Delete(id);
        }

        // --- App Settings ---
        /// <summary>
        /// Trả về cài đặt từ bộ nhớ đệm (không mở DB mỗi lần gọi). Không sửa trực tiếp đối tượng trả về;
        /// hãy dùng SaveSettings.
        /// </summary>
        public AppSettings GetSettings()
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            using var db = OpenDb();
            _cachedSettings = db.GetCollection<AppSettings>("settings").FindById(1) ?? new AppSettings();
            return _cachedSettings;
        }

        public void SaveSettings(AppSettings settings)
        {
            settings.Id = 1;
            settings.MasterPin = string.Empty;
            using var db = OpenDb();
            db.GetCollection<AppSettings>("settings").Upsert(settings);
            _cachedSettings = settings;
        }

        public bool VerifyMasterPin(string pin)
        {
            var settings = GetSettings();
            return SecurityHelper.VerifyPin(pin?.Trim() ?? string.Empty, settings.MasterPinHash, settings.MasterPinSalt);
        }

        public void SetMasterPin(AppSettings settings, string newPin)
        {
            var (hash, salt) = SecurityHelper.HashPin(newPin);
            settings.MasterPinHash = hash;
            settings.MasterPinSalt = salt;
            settings.IsUsingDefaultPin = newPin == AppSettings.DefaultPin;
        }
    }
}

