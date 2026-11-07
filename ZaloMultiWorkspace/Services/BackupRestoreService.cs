using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace ZaloMultiWorkspace.Services
{
    public class BackupRestoreService
    {
        public async Task BackupWorkspaceAsync(string destinationZipPath)
        {
            await Task.Run(() =>
            {
                if (File.Exists(destinationZipPath))
                {
                    File.Delete(destinationZipPath);
                }

                // Nén toàn bộ thư mục dữ liệu ứng dụng (bao gồm ZaloWorkspace.db và Profiles/)
                ZipFile.CreateFromDirectory(
                    DatabaseService.BaseAppDir,
                    destinationZipPath,
                    CompressionLevel.Optimal,
                    includeBaseDirectory: false);
            });
        }

        public async Task RestoreWorkspaceAsync(string sourceZipPath)
        {
            await Task.Run(() =>
            {
                if (!File.Exists(sourceZipPath))
                    throw new FileNotFoundException("Không tìm thấy file sao lưu (.zip).", sourceZipPath);

                Directory.CreateDirectory(DatabaseService.BaseAppDir);
                ZipFile.ExtractToDirectory(sourceZipPath, DatabaseService.BaseAppDir, overwriteFiles: true);
            });
        }
    }
}

