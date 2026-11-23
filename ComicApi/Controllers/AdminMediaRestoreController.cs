using System.IO.Compression;
using ComicApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComicApi.Controllers;

// Công cụ chuyển ảnh một lần; chỉ admin và chỉ khi bật cờ chuyển dữ liệu.
[ApiController]
[Route("api/admin/media-restore")]
[Authorize(Roles = Roles.Admin)]
public class AdminMediaRestoreController(IWebHostEnvironment env, IConfiguration config) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Restore(IFormFile archive, CancellationToken ct)
    {
        if (!config.GetValue<bool>("Migration:MediaRestoreEnabled")) return NotFound();
        using var zip = new ZipArchive(archive.OpenReadStream(), ZipArchiveMode.Read);
        var root = Path.GetFullPath(env.WebRootPath) + Path.DirectorySeparatorChar;
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        var files = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToArray();
        if (files.Length > 5000 || files.Sum(e => e.Length) > 100_000_000)
            return BadRequest(new { message = "Gói ảnh vượt giới hạn." });
        foreach (var entry in files)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!(name.StartsWith("uploads/", StringComparison.Ordinal) ||
                  name.StartsWith("img/genres/", StringComparison.Ordinal)) ||
                name.Split('/').Any(p => p is ".." or "." or "" || p.Contains(':')) ||
                !allowed.Contains(Path.GetExtension(name)) ||
                !Path.GetFullPath(Path.Combine(root, name)).StartsWith(root, StringComparison.Ordinal))
                return BadRequest(new { message = "Đường dẫn ảnh không hợp lệ." });
        }
        foreach (var entry in files)
        {
            var target = Path.Combine(root, entry.FullName.Replace('\\', '/'));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = System.IO.File.Create(target);
            await input.CopyToAsync(output, ct);
        }
        return Ok(new { restored = files.Length });
    }
}
