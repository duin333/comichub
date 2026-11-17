using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace ComicApi.Services;

// Một "cấu hình" xử lý ảnh: ảnh được thu nhỏ cho vừa khung này (không bao giờ phóng to), rồi nén
public record ImageProfile(int MaxWidth, int? MaxHeight, int Quality);

public record OptimizedImage(byte[] Data, string Extension, int Width, int Height);

public static class ImageOptimizer
{
    public static readonly ImageProfile Cover = new(800, 1200, 80);    // ảnh bìa dùng ở trang chi tiết và banner
    public static readonly ImageProfile Thumb = new(320, 480, 75);     // ảnh bìa thu nhỏ dùng ở thẻ truyện
    public static readonly ImageProfile Page = new(1200, null, 80);    // trang truyện: giới hạn chiều rộng, giữ nguyên chiều cao

    // Chống "bom giải nén": một file 2 MB vẫn có thể là ảnh 60 000 x 60 000 điểm ảnh, giải nén ra hàng chục GB RAM.
    // Đọc kích thước từ phần đầu file TRƯỚC, quá giới hạn thì từ chối mà không giải nén
    private const int MaxSide = 30_000;
    private const long MaxPixels = 40_000_000;

    // WebP chỉ cho phép tối đa 16 383 điểm ảnh mỗi cạnh; trang quá dài (webtoon) sẽ lưu JPEG thay thế
    private const int WebpMaxSide = 16_383;

    // Phát hiện ảnh bị cắt dở (tải lên chưa xong, file hỏng). ImageSharp giải mã "nới lỏng": đọc được đến đâu lấy đến đó,
    // nên không kiểm tra thì ảnh dở dang (nửa dưới xám) vẫn được lưu như ảnh thật
    public static bool LooksComplete(ReadOnlySpan<byte> d)
    {
        if (d.Length < 12) return false;

        if (d[0] == 0xFF && d[1] == 0xD8)                                   // JPEG: phải có dấu kết thúc FF D9 gần cuối file
            return d[Math.Max(0, d.Length - 16_384)..].IndexOf(new byte[] { 0xFF, 0xD9 }) >= 0;

        if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)   // PNG: phải có chunk IEND
            return d[Math.Max(0, d.Length - 1_024)..].IndexOf("IEND"u8) >= 0;

        if (d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46)                   // GIF: byte cuối (bỏ các số 0 đệm) phải là ký tự kết thúc 0x3B
        {
            var end = d.Length;
            while (end > 0 && d[end - 1] == 0) end--;
            return end > 0 && d[end - 1] == 0x3B;
        }

        if (d[0] == 0x52 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x46)   // WebP: độ dài khai báo trong tiêu đề phải khớp với file thật
            return d.Length >= (long)BitConverter.ToUInt32(d.Slice(4, 4)) + 8;

        return false;
    }

    // Đọc ảnh MỘT lần rồi tạo ra nhiều bản (mỗi profile một bản). Trả về theo đúng thứ tự profile
    public static async Task<IReadOnlyList<OptimizedImage>> OptimizeAsync(Stream input, params ImageProfile[] profiles)
    {
        try
        {
            byte[] all;
            if (input is MemoryStream own) all = own.ToArray();
            else { using var tmp = new MemoryStream(); input.Position = 0; await input.CopyToAsync(tmp); all = tmp.ToArray(); }
            if (!LooksComplete(all))
                throw new InvalidUploadException("Ảnh bị cắt dở hoặc hỏng (tải lên chưa đầy đủ)");

            input.Position = 0;
            var info = await Image.IdentifyAsync(input);                    // chỉ đọc phần đầu file
            if (info.Width > MaxSide || info.Height > MaxSide || (long)info.Width * info.Height > MaxPixels)
                throw new InvalidUploadException(
                    $"Ảnh quá lớn ({info.Width} x {info.Height} điểm ảnh), tối đa {MaxPixels / 1_000_000} triệu điểm ảnh");

            input.Position = 0;
            // MaxFrames = 1: ảnh động (GIF, WebP) chỉ lấy khung hình đầu tiên
            using var image = await Image.LoadAsync(new DecoderOptions { MaxFrames = 1 }, input);
            image.Mutate(x => x.AutoOrient());                              // xoay đúng chiều theo thông tin chụp (EXIF)

            var results = new List<OptimizedImage>();
            foreach (var p in profiles)
            {
                // Chỉ thu nhỏ, không phóng to ảnh vốn đã nhỏ
                var scale = Math.Min(1.0, (double)p.MaxWidth / image.Width);
                if (p.MaxHeight is int mh) scale = Math.Min(scale, (double)mh / image.Height);
                var w = Math.Max(1, (int)Math.Round(image.Width * scale));
                var h = Math.Max(1, (int)Math.Round(image.Height * scale));

                using var copy = image.Clone(ctx =>
                {
                    if (w != image.Width || h != image.Height) ctx.Resize(w, h, KnownResamplers.Lanczos3);
                });

                using var ms = new MemoryStream();
                string ext;
                if (w > WebpMaxSide || h > WebpMaxSide)
                {
                    await copy.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 85, SkipMetadata = true });
                    ext = ".jpg";
                }
                else
                {
                    // SkipMetadata: bỏ EXIF (vị trí GPS, tên máy ảnh...) để không lộ thông tin riêng tư của người tải lên
                    await copy.SaveAsWebpAsync(ms, new WebpEncoder
                    {
                        Quality = p.Quality,
                        FileFormat = WebpFileFormatType.Lossy,
                        SkipMetadata = true
                    });
                    ext = ".webp";
                }

                results.Add(new OptimizedImage(ms.ToArray(), ext, w, h));
            }

            return results;
        }
        catch (InvalidUploadException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // Ảnh hỏng, bị cắt dở hoặc định dạng lạ: báo lỗi dữ liệu (400) chứ không để thành lỗi máy chủ (500)
            throw new InvalidUploadException("Không đọc được ảnh (file bị hỏng hoặc định dạng không hỗ trợ)");
        }
    }
}