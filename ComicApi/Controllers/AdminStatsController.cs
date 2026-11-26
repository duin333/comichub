using System.Diagnostics;
using ComicApi.Data;
using ComicApi.Models;
using ComicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicApi.Controllers;

[ApiController]
[Route("api/admin/stats")]
[Authorize(Roles = Roles.Admin)]
public class AdminStatsController : ControllerBase
{
    // Biểu đồ theo ngày tính theo giờ Việt Nam (UTC+7); dữ liệu trong database lưu giờ UTC
    private const int VietnamOffsetHours = 7;

    private readonly AppDbContext _db;
    private readonly ICatalogCache _cache;
    private readonly IWebHostEnvironment _env;
    private readonly TimeProvider _clock;

    public AdminStatsController(AppDbContext db, ICatalogCache cache, IWebHostEnvironment env, TimeProvider? clock = null)
    {
        _db = db;
        _cache = cache;
        _env = env;
        _clock = clock ?? TimeProvider.System;
    }

    private record DayRow(DateTime Day, int Count);

    // GET /api/admin/stats/overview?days=14
    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] int days = 14)
    {
        days = Math.Clamp(days, 1, 90);

        var utcNow = _clock.GetUtcNow().UtcDateTime;
        var offset = TimeSpan.FromHours(VietnamOffsetHours);
        var today = (utcNow + offset).Date;                    // "hôm nay" theo giờ Việt Nam
        var firstDay = today.AddDays(-(days - 1));
        var fromUtc = firstDay - offset;                       // 00:00 giờ Việt Nam của ngày đầu, đổi về UTC
        var since24h = utcNow.AddHours(-24);

        // ----- Tổng số (các truy vấn chạy lần lượt vì DbContext không dùng chung giữa nhiều luồng) -----
        var comments = await _db.Comments.AsNoTracking().CountAsync(c => c.ParentId == null);
        var replies = await _db.Comments.AsNoTracking().CountAsync(c => c.ParentId != null);
        var totals = new
        {
            users = await _db.Users.AsNoTracking().CountAsync(),
            lockedUsers = await _db.Users.AsNoTracking().CountAsync(u => u.IsLocked),
            comics = await _db.Comics.AsNoTracking().CountAsync(),
            chapters = await _db.Chapters.AsNoTracking().CountAsync(c => !c.IsHidden),
            hiddenChapters = await _db.Chapters.AsNoTracking().CountAsync(c => c.IsHidden),
            comments,
            replies,
            ratings = await _db.Set<Rating>().AsNoTracking().CountAsync(),
            follows = await _db.Follows.AsNoTracking().CountAsync(),
            readingHistories = await _db.ReadingHistories.AsNoTracking().CountAsync(),
            commentLikes = await _db.Set<CommentLike>().AsNoTracking().CountAsync(),
            totalViews = await _db.Comics.AsNoTracking().SumAsync(c => c.ViewCount),
            commentsLast24h = await _db.Comments.AsNoTracking().CountAsync(c => c.CreatedAt >= since24h),
            newUsersLast24h = await _db.Users.AsNoTracking().CountAsync(u => u.CreatedAt >= since24h)
        };

        // ----- Theo từng ngày (gom nhóm ngay trong database, chỉ trong khoảng thời gian được chọn) -----
        var commentsByDay = await _db.Comments.AsNoTracking()
            .Where(c => c.CreatedAt >= fromUtc)
            .GroupBy(c => c.CreatedAt.AddHours(VietnamOffsetHours).Date)
            .Select(g => new DayRow(g.Key, g.Count()))
            .ToListAsync();
        var usersByDay = await _db.Users.AsNoTracking()
            .Where(u => u.CreatedAt >= fromUtc)
            .GroupBy(u => u.CreatedAt.AddHours(VietnamOffsetHours).Date)
            .Select(g => new DayRow(g.Key, g.Count()))
            .ToListAsync();
        var ratingsByDay = await _db.Set<Rating>().AsNoTracking()
            .Where(r => r.CreatedAt >= fromUtc)
            .GroupBy(r => r.CreatedAt.AddHours(VietnamOffsetHours).Date)
            .Select(g => new DayRow(g.Key, g.Count()))
            .ToListAsync();

        var cSeries = StatsMath.FillDays(firstDay, days, commentsByDay.Select(r => (r.Day, r.Count)));
        var uSeries = StatsMath.FillDays(firstDay, days, usersByDay.Select(r => (r.Day, r.Count)));
        var rSeries = StatsMath.FillDays(firstDay, days, ratingsByDay.Select(r => (r.Day, r.Count)));
        var daily = Enumerable.Range(0, days).Select(i => new
        {
            date = firstDay.AddDays(i).ToString("yyyy-MM-dd"),
            comments = cSeries[i],
            users = uSeries[i],
            ratings = rSeries[i]
        }).ToList();

        // ----- Truyện xem nhiều nhất, kèm số chương, lượt theo dõi, điểm đánh giá -----
        var top = await _db.Comics.AsNoTracking()
            .OrderByDescending(c => c.ViewCount).ThenBy(c => c.Id)
            .Take(10)
            .Select(c => new { c.Id, c.Title, c.ViewCount })
            .ToListAsync();
        var ids = top.Select(t => t.Id).ToList();

        var chapterCounts = (await _db.Chapters.AsNoTracking()
            .Where(ch => ids.Contains(ch.ComicId) && !ch.IsHidden)
            .GroupBy(ch => ch.ComicId)
            .Select(g => new { ComicId = g.Key, N = g.Count() })
            .ToListAsync()).ToDictionary(x => x.ComicId, x => x.N);
        var followCounts = (await _db.Follows.AsNoTracking()
            .Where(f => ids.Contains(f.ComicId))
            .GroupBy(f => f.ComicId)
            .Select(g => new { ComicId = g.Key, N = g.Count() })
            .ToListAsync()).ToDictionary(x => x.ComicId, x => x.N);
        var ratingStats = (await _db.Set<Rating>().AsNoTracking()
            .Where(r => ids.Contains(r.ComicId))
            .GroupBy(r => r.ComicId)
            .Select(g => new { ComicId = g.Key, N = g.Count(), Avg = g.Average(r => (double)r.Stars) })
            .ToListAsync()).ToDictionary(x => x.ComicId);

        var topComics = top.Select(t => new
        {
            id = t.Id,
            title = t.Title,
            viewCount = t.ViewCount,
            chapterCount = chapterCounts.GetValueOrDefault(t.Id),
            followCount = followCounts.GetValueOrDefault(t.Id),
            ratingCount = ratingStats.TryGetValue(t.Id, out var rs) ? rs.N : 0,
            ratingAverage = ratingStats.TryGetValue(t.Id, out var rs2) ? Math.Round(rs2.Avg, 2) : (double?)null
        }).ToList();

        // ----- Người bình luận nhiều nhất -----
        var commenters = await _db.Comments.AsNoTracking()
            .GroupBy(c => c.UserId)
            .Select(g => new { UserId = g.Key, N = g.Count() })
            .OrderByDescending(x => x.N).ThenBy(x => x.UserId)
            .Take(5)
            .ToListAsync();
        var commenterIds = commenters.Select(c => c.UserId).ToList();
        var names = (await _db.Users.AsNoTracking()
            .Where(u => commenterIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username })
            .ToListAsync()).ToDictionary(x => x.Id, x => x.Username);
        var topCommenters = commenters.Select(c => new
        {
            userId = c.UserId,
            username = names.GetValueOrDefault(c.UserId, "(đã xóa)"),
            comments = c.N
        }).ToList();

        // ----- Bộ nhớ đệm (12.2) và máy chủ -----
        var cs = _cache.Stats;
        using var process = Process.GetCurrentProcess();

        return Ok(new
        {
            generatedAt = utcNow,
            days,
            totals,
            daily,
            topComics,
            topCommenters,
            cache = new
            {
                hits = cs.Hits,
                misses = cs.Misses,
                invalidations = cs.Invalidations,
                entries = cs.Entries,
                hitRate = StatsMath.HitRate(cs.Hits, cs.Misses)
            },
            system = new
            {
                uptimeSeconds = (long)(DateTime.Now - process.StartTime).TotalSeconds,
                heapMb = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1),
                workingSetMb = Math.Round(process.WorkingSet64 / 1048576.0, 1),
                dotnet = Environment.Version.ToString(),
                environment = _env.EnvironmentName
            }
        });
    }
}