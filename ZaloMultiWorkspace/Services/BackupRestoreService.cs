using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace ZaloMultiWorkspace.Services
{
    public class BackupResult
    {
        public int FileCount { get; set; }
        public List<string> SkippedFiles { get; } = new();
    }

    public class BackupRestoreService
    {
        // Thư mục cache của Chromium: tự tạo lại được, rất nặng và hay bị khóa -> không sao lưu
        private static readonly HashSet<string> ExcludedDirNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache",
            "GrShaderCache", "GraphiteDawnCache", "ShaderCache", "CacheStorage", "ScriptCache",
            "Crashpad", "component_crx_cache", "extensions_crx_cache"
        };

        private const string RestoreRollbackDirName = ".restore_rollback";
        private const string RestoreTempDirName = ".restore_temp";

        public async Task<BackupResult> BackupWorkspaceAsync(string destinationZipPath)
        {
            return await Task.Run(() =>
            {
                var result = new BackupResult();
                string baseDir = Path.GetFullPath(DatabaseService.BaseAppDir);
                string destFull = Path.GetFullPath(destinationZipPath);
                string tempZip = destFull + ".tmp";

                if (File.Exists(tempZip))
                    File.Delete(tempZip);

                using (var zipStream = new FileStream(tempZip, FileMode.CreateNew, FileAccess.Write))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    foreach (string file in EnumerateBackupFiles(baseDir))
                    {
                        string full = Path.GetFullPath(file);
                        if (string.Equals(full, destFull, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(full, tempZip, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string entryName = Path.GetRelativePath(baseDir, full).Replace('\\', '/');
                        try
                        {
                            // Mở với FileShare.ReadWrite để đọc được cả file đang được tiến trình khác mở
                            using var src = new FileStream(full, FileMode.Open, FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete);
                            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                            entry.LastWriteTime = File.GetLastWriteTime(full);
                            using var dst = entry.Open();
                            src.CopyTo(dst);
                            result.FileCount++;
                        }
                        catch (IOException)
                        {
                            result.SkippedFiles.Add(entryName);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            result.SkippedFiles.Add(entryName);
                        }
                    }
                }

                File.Move(tempZip, destFull, overwrite: true);
                return result;
            });
        }

        private static IEnumerable<string> EnumerateBackupFiles(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string[] files;
                string[] subDirs;
                try
                {
                    files = Directory.GetFiles(dir);
                    subDirs = Directory.GetDirectories(dir);
                }
                catch
                {
                    continue;
                }

                foreach (var f in files)
                {
                    string name = Path.GetFileName(f);
                    if (name.Equals("lockfile", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".zip.tmp", StringComparison.OrdinalIgnoreCase))
                        continue;
                    yield return f;
                }

                foreach (var d in subDirs)
                {
                    string name = Path.GetFileName(d);
                    if (ExcludedDirNames.Contains(name) ||
                        name.Equals(RestoreRollbackDirName, StringComparison.OrdinalIgnoreCase) ||
                        name.Equals(RestoreTempDirName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    stack.Push(d);
                }
            }
        }

        /// <summary>
        /// Khôi phục an toàn: giải nén ra thư mục tạm, kiểm tra hợp lệ, chuyển dữ liệu hiện tại sang
        /// thư mục rollback rồi mới thay thế. Nếu lỗi giữa chừng sẽ hoàn tác về dữ liệu cũ.
        /// </summary>
        public async Task RestoreWorkspaceAsync(string sourceZipPath)
        {
            await Task.Run(() =>
            {
                if (!File.Exists(sourceZipPath))
                    throw new FileNotFoundException("Không tìm thấy file sao lưu (.zip).", sourceZipPath);

                string baseDir = DatabaseService.BaseAppDir;
                Directory.CreateDirectory(baseDir);

                string tempDir = Path.Combine(baseDir, RestoreTempDirName);
                string rollbackDir = Path.Combine(baseDir, RestoreRollbackDirName);
                SafeDeleteDirectory(tempDir);
                SafeDeleteDirectory(rollbackDir);

                // 1. Giải nén ra thư mục tạm (ExtractToDirectory tự chặn đường dẫn kiểu ../)
                ZipFile.ExtractToDirectory(sourceZipPath, tempDir, overwriteFiles: true);

                string restoredDb = Path.Combine(tempDir, Path.GetFileName(DatabaseService.DbFilePath));
                if (!File.Exists(restoredDb))
                {
                    SafeDeleteDirectory(tempDir);
                    throw new InvalidDataException("File .zip không phải bản sao lưu hợp lệ (thiếu ZaloWorkspace.db).");
                }

                // 2. Chuyển dữ liệu hiện tại sang thư mục rollback
                Directory.CreateDirectory(rollbackDir);
                var movedItems = new List<(string From, string To)>();
                var restoredItems = new List<string>();
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(baseDir).ToList())
                    {
                        string name = Path.GetFileName(entry);
                        if (name.Equals(RestoreTempDirName, StringComparison.OrdinalIgnoreCase) ||
                            name.Equals(RestoreRollbackDirName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string target = Path.Combine(rollbackDir, name);
                        if (Directory.Exists(entry))
                            Directory.Move(entry, target);
                        else
                            File.Move(entry, target);
                        movedItems.Add((entry, target));
                    }

                    // 3. Đưa dữ liệu khôi phục vào vị trí chính
                    foreach (var entry in Directory.EnumerateFileSystemEntries(tempDir).ToList())
                    {
                        string target = Path.Combine(baseDir, Path.GetFileName(entry));
                        if (Directory.Exists(entry))
                            Directory.Move(entry, target);
                        else
                            File.Move(entry, target);
                        restoredItems.Add(target);
                    }
                }
                catch
                {
                    // Hoàn tác: xóa phần đã khôi phục dở, trả lại dữ liệu cũ
                    foreach (var restored in restoredItems)
                    {
                        if (Directory.Exists(restored)) SafeDeleteDirectory(restored);
                        else try { File.Delete(restored); } catch { }
                    }
                    foreach (var (from, to) in movedItems)
                    {
                        try
                        {
                            if (Directory.Exists(to)) Directory.Move(to, from);
                            else if (File.Exists(to)) File.Move(to, from, overwrite: true);
                        }
                        catch { }
                    }
                    SafeDeleteDirectory(tempDir);
                    throw;
                }

                SafeDeleteDirectory(tempDir);
                SafeDeleteDirectory(rollbackDir);
                Directory.CreateDirectory(DatabaseService.BaseProfileDir);
            });
        }

        private static void SafeDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Bỏ qua - sẽ được dọn ở lần sau
            }
        }
    }
}
