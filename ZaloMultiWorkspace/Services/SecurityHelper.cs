using System;
using System.Security.Cryptography;
using System.Text;

namespace ZaloMultiWorkspace.Services
{
    /// <summary>
    /// Mã hóa bí mật (mật khẩu Proxy) bằng Windows DPAPI theo tài khoản Windows hiện tại
    /// và băm mã PIN bằng PBKDF2 để không lưu dạng văn bản thô trong LiteDB.
    /// </summary>
    public static class SecurityHelper
    {
        private const string ProtectedPrefix = "dpapi:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ZaloMultiWorkspace.v1"); // Không đổi: mật khẩu proxy đã lưu sẽ không giải mã được

        public static string Protect(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            byte[] data = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            return ProtectedPrefix + Convert.ToBase64String(encrypted);
        }

        /// <summary>
        /// Giải mã chuỗi đã mã hóa. Trả về chuỗi rỗng nếu không giải mã được
        /// (ví dụ khôi phục backup trên máy / tài khoản Windows khác).
        /// </summary>
        public static string Unprotect(string? protectedText)
        {
            if (string.IsNullOrEmpty(protectedText))
                return string.Empty;

            if (!protectedText.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
                return protectedText; // Dữ liệu cũ chưa mã hóa

            try
            {
                byte[] encrypted = Convert.FromBase64String(protectedText.Substring(ProtectedPrefix.Length));
                byte[] data = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return string.Empty;
            }
        }

        private const int PinIterations = 100_000;

        public static (string Hash, string Salt) HashPin(string pin)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, PinIterations, HashAlgorithmName.SHA256, 32);
            return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
        }

        public static bool VerifyPin(string pin, string hashBase64, string saltBase64)
        {
            if (string.IsNullOrEmpty(hashBase64) || string.IsNullOrEmpty(saltBase64))
                return false;
            try
            {
                byte[] salt = Convert.FromBase64String(saltBase64);
                byte[] expected = Convert.FromBase64String(hashBase64);
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(pin ?? string.Empty, salt, PinIterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }
    }
}
