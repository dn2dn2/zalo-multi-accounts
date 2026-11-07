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
            "ZaloMultiWorkspace");

        public static string BaseProfileDir => Path.Combine(BaseAppDir, "Profiles");

        public static string DbFilePath => Path.Combine(BaseAppDir, "ZaloWorkspace.db");

        public DatabaseService()
        {
            Directory.CreateDirectory(BaseAppDir);
            Directory.CreateDirectory(BaseProfileDir);
            EnsureSeedData();
        }

        private LiteDatabase OpenDb()
        {
            return new LiteDatabase($"Filename={DbFilePath};Connection=shared");
        }

        private void EnsureSeedData()
        {
            using var db = OpenDb();
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
                    AutoHibernateMinutes = 30,
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
                    AutoHibernateMinutes = 20,
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
                    AutoHibernateMinutes = 15,
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

            var settings = db.GetCollection<AppSettings>("settings");
            if (settings.Count() == 0)
            {
                settings.Insert(new AppSettings());
            }
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
        public AppSettings GetSettings()
        {
            using var db = OpenDb();
            return db.GetCollection<AppSettings>("settings").FindById(1) ?? new AppSettings();
        }

        public void SaveSettings(AppSettings settings)
        {
            settings.Id = 1;
            using var db = OpenDb();
            db.GetCollection<AppSettings>("settings").Upsert(settings);
        }
    }
}

