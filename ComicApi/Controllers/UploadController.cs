using System.Text.RegularExpressions;
using ComicApi.DTOs.Admin;
using ComicApi.Models;
using ComicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComicApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Admin)]
public class UploadController : ControllerBase
{
    private const long MaxCoverBytes = 2 * 1024 * 1024;   // 2 MB
    private const long MaxPageBytes = 5 * 1024 * 1024;    // 5 MB mỗi trang
    private const int MaxFiles = 50;

    private readonly IFileStorage _files;

    public UploadController(IFileStorage files) => _files = files;

    [HttpPost("cover")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadCover([FromForm] UploadCoverRequest request)
    {
        try
        {
            var url = await _files.SaveImageAsync(request.File, "covers", MaxCoverBytes);
            return Ok(new { url });
        }
        catch (InvalidUploadException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("chapter-images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(200_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 200_000_000)]
    public async Task<IActionResult> UploadChapterImages([FromForm] UploadChapterImagesRequest request)
    {
        var files = request.Files;

        if (files.Count == 0)
            return BadRequest(new { message = "Chưa chọn file nào" });
        if (files.Count > MaxFiles)
            return BadRequest(new { message = $"Tối đa {MaxFiles} ảnh mỗi lần tải" });

        // Sắp theo tên file kiểu "tự nhiên": 1, 2, 10 chứ không phải 1, 10, 2
        var ordered = files
            .OrderBy(f => NaturalKey(f.FileName), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var urls = new List<string>();
        try
        {
            foreach (var f in ordered)
                urls.Add(await _files.SaveImageAsync(f, "chapters", MaxPageBytes));
        }
        catch (InvalidUploadException ex)
        {
            // Có 1 file lỗi thì xóa luôn những file đã lưu trước đó, tránh để file rác
            foreach (var u in urls) _files.DeleteLocal(u);
            return BadRequest(new { message = ex.Message });
        }

        return Ok(new { urls });
    }

    private static string NaturalKey(string name) =>
        Regex.Replace(name, @"\d+", m => m.Value.PadLeft(10, '0'));

    // POST /api/Upload/optimize-existing?folder=seed/covers&limit=100&sql=true
    // Chuyển ảnh CÓ SẴN sang WebP (kèm ảnh thu nhỏ cho thư mục bìa), giữ nguyên file gốc.
    // sql=true: trả về văn bản thuần chứa các câu UPDATE để chạy trong Workbench
    [HttpPost("optimize-existing")]
    public async Task<IActionResult> OptimizeExisting(
        [FromQuery] string folder = "seed/covers", [FromQuery] int limit = 100, [FromQuery] bool sql = false)
    {
        try
        {
            var report = await _files.OptimizeFolderAsync(folder, Math.Clamp(limit, 1, 500));
            if (!sql) return Ok(report);

            var text = report.Sql + string.Concat(report.Errors.Select(e => $"-- LỖI: {e}\n"));
            return Content(text, "text/plain; charset=utf-8");
        }
        catch (InvalidUploadException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}