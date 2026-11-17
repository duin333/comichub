using System.Text;
using System.Text.RegularExpressions;

namespace ComicApi.Services;

public class InvalidUploadException : Exception
{
    public InvalidUploadException(string message) : base(message) { }
}

public record OptimizedItem(string From, string To, long BeforeBytes, long AfterBytes);

public class OptimizeReport
{
    public int Converted { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public long BytesBefore { get; set; }
    public long BytesAfter { get; set; }
    public List<OptimizedItem> Items { get; } = new();
    public List<string> Errors { get; } = new();
    public string Sql { get; set; } = "";
}

public interface IFileStorage
{
    Task<string> SaveImageAsync(IFormFile file, string folder, long maxBytes);
    void DeleteLocal(string? url);
    bool IsAllowedUrl(string? url);
    Task<OptimizeReport> OptimizeFolderAsync(string folder, int limit);
}

public class FileStorage : IFileStorage
{
    private const string UrlPrefix = "/uploads/";
    private const string ThumbFolder = "thumbs";

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    private static readonly Regex SafeFolder = new(@"^[a-z0-9_-]+(/[a-z0-9_-]+)*$", RegexOptions.Compiled);

    private readonly string _root;

    public FileStorage(IWebHostEnvironment env)
    {
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        _root = Path.GetFullPath(Path.Combine(webRoot, "uploads"));
        Directory.CreateDirectory(_root);
    }

    // Ảnh bìa (folder = "covers") được lưu 2 bản: ảnh chính .webp tối đa 800x1200 và ảnh thu nhỏ thumbs/<cùng tên>.webp rộng 320.
    // Ảnh trang truyện được thu nhỏ về rộng tối đa 1200 rồi lưu .webp. Mọi ảnh đều bị bỏ EXIF.
    public async Task<string> SaveImageAsync(IFormFile file, string folder, long maxBytes)
    {
        if (file.Length == 0)
            throw new InvalidUploadException($"File '{file.FileName}' bị rỗng");

        if (file.Length > maxBytes)
            throw new InvalidUploadException(
                $"File '{file.FileName}' vượt quá {maxBytes / 1024 / 1024} MB");

        var ext = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidUploadException(
                $"File '{file.FileName}' không hợp lệ, chỉ nhận jpg, jpeg, png, webp, gif");

        // Đọc cả file vào bộ nhớ (đã giới hạn maxBytes). Xử lý xong, thành công mới ghi xuống đĩa,
        // nên một ảnh hỏng không bao giờ để lại file dở dang
        await using var buffer = new MemoryStream((int)file.Length);
        await using (var src = file.OpenReadStream())
            await src.CopyToAsync(buffer);

        // Đọc 12 byte đầu để chắc chắn đây đúng là ảnh (chống đổi đuôi .exe thành .jpg)
        var header = new byte[12];
        buffer.Position = 0;
        var read = await buffer.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        if (read < header.Length || !LooksLikeImage(header))
            throw new InvalidUploadException($"File '{file.FileName}' không phải ảnh hợp lệ");

        var isCover = folder == "covers";
        var profiles = isCover ? new[] { ImageOptimizer.Cover, ImageOptimizer.Thumb } : new[] { ImageOptimizer.Page };

        IReadOnlyList<OptimizedImage> results;
        try
        {
            results = await ImageOptimizer.OptimizeAsync(buffer, profiles);
        }
        catch (InvalidUploadException ex)
        {
            throw new InvalidUploadException($"File '{file.FileName}': {ex.Message}");
        }

        // Đặt tên ngẫu nhiên để không trùng tên và không dùng tên file do người dùng gửi
        var id = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);

        var main = results[0];
        var mainPath = Path.Combine(dir, id + main.Extension);
        await File.WriteAllBytesAsync(mainPath, main.Data);

        if (isCover)
        {
            try
            {
                var thumbDir = Path.Combine(dir, ThumbFolder);
                Directory.CreateDirectory(thumbDir);
                await File.WriteAllBytesAsync(Path.Combine(thumbDir, id + ".webp"), results[1].Data);
            }
            catch
            {
                try { File.Delete(mainPath); } catch { }      // ghi ảnh thu nhỏ lỗi thì bỏ luôn ảnh chính, không để lại nửa vời
                throw;
            }
        }

        return $"{UrlPrefix}{folder}/{id}{main.Extension}";
    }

    public void DeleteLocal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(UrlPrefix)) return;

        var relative = url.Substring(UrlPrefix.Length).Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_root, relative));

        // Chặn kiểu đường dẫn "../" để không xóa nhầm file ngoài thư mục uploads
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar)) return;

        try
        {
            if (File.Exists(full)) File.Delete(full);

            // Ảnh bìa có kèm ảnh thu nhỏ cùng tên trong thư mục thumbs: xóa luôn cho khỏi để rác
            var parent = Path.GetDirectoryName(full)!;
            if (Path.GetFileName(parent) != ThumbFolder)
            {
                var thumb = Path.Combine(parent, ThumbFolder, Path.GetFileNameWithoutExtension(full) + ".webp");
                if (File.Exists(thumb)) File.Delete(thumb);
            }
        }
        catch { /* xóa file thất bại thì bỏ qua, không làm hỏng thao tác chính */ }
    }

    public bool IsAllowedUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && url.Length <= 500
        && ((url.StartsWith(UrlPrefix) && !url.Contains(".."))
            || url.StartsWith("https://")
            || url.StartsWith("http://"));

    // Chuyển các ảnh đã có sẵn (jpg, jpeg, png, gif) trong một thư mục con của uploads sang .webp (kèm ảnh thu nhỏ nếu là thư mục bìa).
    // File gốc được GIỮ NGUYÊN; kết quả là một đoạn SQL để bạn tự chạy cập nhật đường dẫn trong database. Chạy lại nhiều lần vẫn an toàn.
    public async Task<OptimizeReport> OptimizeFolderAsync(string folder, int limit)
    {
        if (!SafeFolder.IsMatch(folder) || folder.Split('/').Contains(ThumbFolder))
            throw new InvalidUploadException("Tên thư mục không hợp lệ (chỉ chữ thường, số, - _ và dấu /)");

        var dir = Path.GetFullPath(Path.Combine(_root, folder.Replace('/', Path.DirectorySeparatorChar)));
        if (!dir.StartsWith(_root + Path.DirectorySeparatorChar) || !Directory.Exists(dir))
            throw new InvalidUploadException($"Không có thư mục uploads/{folder}");

        var isCover = folder.EndsWith("covers");
        var profiles = isCover ? new[] { ImageOptimizer.Cover, ImageOptimizer.Thumb } : new[] { ImageOptimizer.Page };
        var report = new OptimizeReport();
        var sql = new StringBuilder();
        var thumbDir = Path.Combine(dir, ThumbFolder);

        foreach (var file in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (report.Converted >= limit) break;

            var ext = Path.GetExtension(file);
            var stem = Path.GetFileNameWithoutExtension(file);
            var isWebp = ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
            if (!AllowedExtensions.Contains(ext)) continue;

            // File .webp do chính công cụ này tạo từ ảnh gốc cùng tên (a.jpg → a.webp): bỏ qua im lặng, không tính vào báo cáo
            if (isWebp && new[] { ".jpg", ".jpeg", ".png", ".gif" }.Any(e => File.Exists(Path.Combine(dir, stem + e)))) continue;

            var webpPath = isWebp ? file : Path.Combine(dir, stem + ".webp");
            var thumbPath = Path.Combine(thumbDir, stem + ".webp");

            // Đã xong từ trước thì bỏ qua: không có ảnh cần đổi sang webp, và (với thư mục bìa) ảnh thu nhỏ đã có
            var needWebp = !isWebp && !File.Exists(webpPath);
            var needThumb = isCover && !File.Exists(thumbPath);
            if (!needWebp && !needThumb) { report.Skipped++; continue; }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file);
                if (bytes.Length < 12 || !LooksLikeImage(bytes)) throw new InvalidUploadException("không phải ảnh hợp lệ");

                using var ms = new MemoryStream(bytes);
                var results = await ImageOptimizer.OptimizeAsync(ms, profiles);

                long after = 0;
                if (needWebp && results[0].Extension == ".webp")
                {
                    await File.WriteAllBytesAsync(webpPath, results[0].Data);
                    after += results[0].Data.Length;
                }
                else if (needWebp)
                {
                    throw new InvalidUploadException("ảnh quá dài để lưu WebP, giữ nguyên bản gốc");
                }

                if (needThumb)
                {
                    Directory.CreateDirectory(thumbDir);
                    await File.WriteAllBytesAsync(thumbPath, results[1].Data);
                    after += results[1].Data.Length;
                }

                var from = $"{UrlPrefix}{folder}/{Path.GetFileName(file)}";
                var to = $"{UrlPrefix}{folder}/{stem}.webp";
                report.Converted++;
                report.BytesBefore += bytes.Length;
                report.BytesAfter += after;

                if (needWebp)
                {
                    report.Items.Add(new OptimizedItem(from, to, bytes.Length, after));
                    sql.AppendLine(isCover
                        ? $"UPDATE comics SET CoverUrl = '{Esc(to)}' WHERE CoverUrl = '{Esc(from)}';"
                        : $"UPDATE chapter_images SET ImageUrl = '{Esc(to)}' WHERE ImageUrl = '{Esc(from)}';");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                report.Failed++;
                report.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        report.Sql = sql.ToString();
        return report;
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("'", "''");

    private static bool LooksLikeImage(byte[] h) =>
        (h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) ||                                  // JPEG
        (h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) ||                  // PNG
        (h[0] == 0x47 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x38) ||                  // GIF
        (h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46 &&
         h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50);                  // WEBP
}