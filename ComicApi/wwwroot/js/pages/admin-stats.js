(() => {
    if (!Admin.guard()) return;
    Admin.renderNav('stats');

    const $ = id => document.getElementById(id);
    const fmt = n => new Intl.NumberFormat('vi-VN').format(n ?? 0);
    const esc = s => String(s ?? '').replace(/[&<>"']/g,
        c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    let reqId = 0;                      // đánh số mỗi lần tải để bỏ qua kết quả của lần tải cũ đến muộn

    // Mốc cao nhất của trục: chia 4 khoảng bằng nhau và MỖI MỐC LÀ SỐ NGUYÊN (0, 6, 12, 18, 24...),
    // nên không bao giờ bị lặp số như "1 1 1 0 0" khi số liệu nhỏ
    function niceTop(max) {
        const raw = Math.max(1, max) / 4;
        let step;
        if (raw <= 10) step = Math.ceil(raw);
        else {
            const p = Math.pow(10, Math.floor(Math.log10(raw)));
            step = [1, 2, 2.5, 5, 10].map(m => m * p).find(v => raw <= v);
        }
        return step * 4;
    }

    // Biểu đồ cột vẽ bằng SVG (không cần thư viện ngoài, không vi phạm chính sách bảo mật CSP)
    function barChart(el, rows, key, title) {
        const W = 640, H = 220, L = 40, R = 8, T = 14, B = 30;
        const innerW = W - L - R, innerH = H - T - B;
        const total = rows.reduce((s, r) => s + r[key], 0);
        const top = niceTop(Math.max(0, ...rows.map(r => r[key])));
        const slot = innerW / rows.length;
        const bw = Math.max(1, slot * 0.7);
        const y = v => T + innerH - (v / top) * innerH;

        let svg = `<svg viewBox="0 0 ${W} ${H}" role="img" aria-label="${esc(title)}: tổng ${fmt(total)} trong ${rows.length} ngày">`;
        for (let i = 0; i <= 4; i++) {                                   // lưới ngang 4 khoảng
            const v = top * i / 4, yy = y(v);
            svg += `<line class="grid" x1="${L}" x2="${W - R}" y1="${yy.toFixed(1)}" y2="${yy.toFixed(1)}"/>` +
                `<text class="tick" x="${L - 6}" y="${(yy + 4).toFixed(1)}" text-anchor="end">${fmt(Math.round(v))}</text>`;
        }
        const step = Math.ceil(rows.length / 8);                         // chỉ ghi nhãn ngày thưa ra cho khỏi chồng chữ
        rows.forEach((r, i) => {
            const h = (r[key] / top) * innerH;
            const x = L + i * slot + (slot - bw) / 2;
            const [yyyy, mm, dd] = r.date.split('-');
            svg += `<rect class="bar" x="${x.toFixed(1)}" y="${(T + innerH - h).toFixed(1)}" width="${bw.toFixed(1)}" height="${h.toFixed(1)}">` +
                `<title>${dd}/${mm}/${yyyy}: ${fmt(r[key])}</title></rect>`;
            if ((rows.length - 1 - i) % step === 0)
                svg += `<text class="tick" x="${(x + bw / 2).toFixed(1)}" y="${H - 10}" text-anchor="middle">${dd}/${mm}</text>`;
        });
        el.innerHTML = svg + '</svg>';
    }

    function fmtUptime(sec) {
        if (sec < 60) return `${sec} giây`;
        const d = Math.floor(sec / 86400), h = Math.floor(sec % 86400 / 3600), m = Math.floor(sec % 3600 / 60);
        return [d && `${d} ngày`, (d || h) && `${h} giờ`, `${m} phút`].filter(Boolean).join(' ');
    }

    function renderCards(t) {
        const cards = [
            ['Người dùng', t.users, `${fmt(t.lockedUsers)} bị khóa`],
            ['Truyện', t.comics, `Tổng lượt xem ${fmt(t.totalViews)}`],
            ['Chương', t.chapters, `${fmt(t.hiddenChapters)} đang ẩn`],
            ['Bình luận', t.comments + t.replies, `${fmt(t.comments)} gốc, ${fmt(t.replies)} phản hồi`],
            ['Đánh giá', t.ratings, ''],
            ['Theo dõi', t.follows, ''],
            ['Lịch sử đọc', t.readingHistories, ''],
            ['Lượt thích bình luận', t.commentLikes, ''],
            ['Bình luận 24 giờ qua', t.commentsLast24h, ''],
            ['Người dùng mới 24 giờ qua', t.newUsersLast24h, '']
        ];
        $('statCards').innerHTML = cards.map(([label, n, sub]) => `
      <div class="col-6 col-md-4 col-lg-3">
        <div class="stat-card">
          <div class="stat-num">${fmt(n)}</div>
          <div class="stat-label">${label}</div>
          ${sub ? `<div class="small text-muted mt-1">${sub}</div>` : ''}
        </div>
      </div>`).join('');
    }

    function render(d) {
        renderCards(d.totals);
        barChart($('chartComments'), d.daily, 'comments', 'Bình luận mỗi ngày');
        barChart($('chartUsers'), d.daily, 'users', 'Người dùng mới mỗi ngày');
        barChart($('chartRatings'), d.daily, 'ratings', 'Đánh giá mỗi ngày');

        $('topComicsBody').innerHTML = d.topComics.length ? d.topComics.map((c, i) => `
      <tr>
        <td>${i + 1}</td>
        <td><a href="/comic.html?id=${Number(c.id)}">${esc(c.title)}</a></td>
        <td class="text-end">${fmt(c.viewCount)}</td>
        <td class="text-end">${fmt(c.chapterCount)}</td>
        <td class="text-end">${fmt(c.followCount)}</td>
        <td class="text-end">${c.ratingAverage == null ? '—' : `${c.ratingAverage.toFixed(1).replace('.', ',')} ★ (${fmt(c.ratingCount)})`}</td>
      </tr>`).join('') : '<tr><td colspan="6" class="text-muted text-center py-3">Chưa có truyện nào.</td></tr>';

        $('topCommentersList').innerHTML = d.topCommenters.length ? d.topCommenters.map((u, i) => `
      <li class="d-flex justify-content-between py-1 border-bottom">
        <span>${i + 1}. ${esc(u.username)}</span><strong>${fmt(u.comments)}</strong>
      </li>`).join('') : '<li class="text-muted">Chưa có bình luận nào.</li>';

        const c = d.cache;
        $('cacheBox').innerHTML = `
      <div class="d-flex justify-content-between"><span>Trả ngay từ bộ nhớ</span><strong>${fmt(c.hits)}</strong></div>
      <div class="d-flex justify-content-between"><span>Phải đọc database</span><strong>${fmt(c.misses)}</strong></div>
      <div class="d-flex justify-content-between"><span>Tỉ lệ trúng</span><strong>${String(c.hitRate).replace('.', ',')}%</strong></div>
      <div class="d-flex justify-content-between"><span>Số mục đang lưu</span><strong>${fmt(c.entries)}</strong></div>
      <div class="d-flex justify-content-between"><span>Số lần xóa (admin sửa dữ liệu)</span><strong>${fmt(c.invalidations)}</strong></div>`;

        const s = d.system;
        $('systemBox').innerHTML = `
      <div class="d-flex justify-content-between"><span>Chạy liên tục</span><strong>${fmtUptime(s.uptimeSeconds)}</strong></div>
      <div class="d-flex justify-content-between"><span>Bộ nhớ đang dùng</span><strong>${String(s.workingSetMb).replace('.', ',')} MB</strong></div>
      <div class="d-flex justify-content-between"><span>Phiên bản .NET</span><strong>${esc(s.dotnet)}</strong></div>
      <div class="d-flex justify-content-between"><span>Môi trường</span><strong>${esc(s.environment)}</strong></div>`;

        $('statsUpdated').textContent = `Cập nhật lúc ${new Date(d.generatedAt).toLocaleTimeString('vi-VN')}`;
    }

    function setBusy(busy) {
        $('statsRefresh').disabled = busy;
        $('statsDays').disabled = busy;
        $('adminRoot').setAttribute('aria-busy', busy ? 'true' : 'false');
    }

    async function load() {
        const my = ++reqId;
        setBusy(true);
        try {
            const d = await Api.get(`/api/admin/stats/overview?days=${encodeURIComponent($('statsDays').value)}`);
            if (my !== reqId) return;                  // đã có lần tải mới hơn, bỏ kết quả cũ này
            $('statsAlert').classList.add('d-none');
            render(d);
        } catch (err) {
            if (my !== reqId) return;
            $('statsAlert').textContent = err.message || 'Không tải được thống kê';
            $('statsAlert').classList.remove('d-none');
        } finally {
            if (my === reqId) setBusy(false);
        }
    }

    $('statsDays').addEventListener('change', load);
    $('statsRefresh').addEventListener('click', load);
    load();
})();