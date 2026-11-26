namespace ComicApi.Services;

public static class StatsMath
{
    // Trả về đúng `days` ngày liên tiếp (cũ → mới) bắt đầu từ firstDay; ngày nào không có số liệu thì là 0.
    // Nhờ vậy biểu đồ luôn đủ cột, không bị "mất" những ngày không có hoạt động
    public static List<int> FillDays(DateTime firstDay, int days, IEnumerable<(DateTime Day, int Count)> counts)
    {
        var byDay = new Dictionary<DateTime, int>();
        foreach (var (day, count) in counts)
            byDay[day.Date] = byDay.GetValueOrDefault(day.Date) + count;

        var result = new List<int>(days);
        for (var i = 0; i < days; i++)
            result.Add(byDay.GetValueOrDefault(firstDay.Date.AddDays(i)));
        return result;
    }

    // Tỉ lệ cache trả được ngay, tính theo phần trăm (0 khi chưa có lượt nào)
    public static double HitRate(long hits, long misses) =>
        hits + misses == 0 ? 0 : Math.Round(100.0 * hits / (hits + misses), 1);
}