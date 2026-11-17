# AGENTS.md — ComicHub (đồ án web truyện tranh)

## 1. Mục đích
ComicHub là web đọc truyện tranh (đồ án môn học, một sinh viên làm). Ba vai trò: **khách** (đọc), **người đọc** (theo dõi, lịch sử, bình luận/trả lời/thích, đánh giá sao, chatbot AI), **admin** (quản lý truyện, chương, thể loại, người dùng, bình luận).
Người dùng giao tiếp bằng tiếng Việt, thích làm từng bước có bảng kiểm tra. Đã quyết định dừng ở mức đủ để báo cáo (mục 7).

## 2. Tech stack, cấu trúc
- Backend: ASP.NET Core 8 REST API, EF Core + Pomelo, MySQL (db `dbtruyentranh`), JWT + BCrypt. Luồng: `Controllers → Repositories → AppDbContext`.
- Frontend: HTML/CSS/JS thuần (không framework, không bundler) trong `wwwroot/`. Bootstrap 5.3.3 TỰ LƯU ở `wwwroot/lib/bootstrap/` (không dùng CDN); phông Google Fonts.
- Chatbot: gọi mô hình có sẵn qua OpenRouter bằng HTTP (không train, không Python, không tool calling); đoán ý định bằng từ khóa trong C#, lấy dữ liệu thật từ DB ghép vào prompt.
- Thư mục chính: `Controllers/` (cả `Admin*`), `Repositories/`, `Services/` (JWT, FileStorage, CatalogCache, `Chat/`, LLM client), `Infrastructure/` (log ra file, bắt lỗi toàn cục, header bảo mật, health, filter xóa cache), `Extensions/` (JWT, rate limit), `Models/`, `DTOs/`, `Data/`, `Migrations/`, `wwwroot/` (`*.html`, `admin/`, `js/api|utils|pages`, `css/site.css`, `uploads/`).
- `ComicApi.AuthTests/`: dự án console kiểm thử phân quyền, ngang hàng project API. `TASKS.md` ở gốc repo ghi tiến độ. Repo GitHub private `duin333/ComicApi`.

## 3. Build và chạy
- Windows + Visual Studio 2022 + MySQL 8. Chạy: F5/Ctrl+F5 hoặc `dotnet run` → `https://localhost:7046` (Swagger `/swagger` chỉ khi Development).
- Migration: Package Manager Console `Add-Migration Ten` / `Update-Database` (hoặc `dotnet ef`).
- Bí mật CHỈ trong User Secrets (không commit): `ConnectionStrings:Default`, `Jwt:Key` (≥32 ký tự), khóa API OpenRouter (xem tên section trong code: `Gemini:*` hoặc `OpenRouter:*`).
- `appsettings.json` chỉ chứa cấu hình không bí mật: Jwt Issuer/Audience/ExpireMinutes, tên model, `RateLimits`, `Security:CspMode` (hiện `Enforce`), `Cache`, `Logging`.
- Dữ liệu mẫu: chạy trong Workbench `seed_danh_gia_binh_luan.sql` rồi `seed_phan_hoi_luot_thich.sql` (chạy lại được). Tài khoản: `admin/Admin@123`; `reader1`, `docgia01..30` / `Reader@123`; `docgia99` bị khóa.
- Test phân quyền (API phải đang chạy): `dotnet run --project ComicApi.AuthTests -- --url https://localhost:7046` → 206 phép thử, ghi `auth-test-report.md`. Mỗi lần đăng nhập ~8 lần: khi chạy test, nâng `RateLimits:Login:PermitLimit` (vd 50) trong `appsettings.Development.json`. Mỗi lần để lại 1 user `perm_*`: `DELETE FROM users WHERE Username LIKE 'perm\_%';`.
- `/health` (còn sống), `/health/ready` (MySQL + thư mục uploads). Log: `logs/comichub-yyyyMMdd.log` (đã `.gitignore`).

## 4. Quy ước và quyết định đã thống nhất
- Comment, thông báo lỗi, tên test bằng tiếng Việt. Commit `Giai doan X.Y: mo ta` (không dấu), rồi tick `TASKS.md`.
- Bình luận CHỈ 2 cấp (đã quyết định không làm cây): trả lời một phản hồi thì gắn vào bình luận gốc + `ReplyToUserId` (@tên).
- Khóa tài khoản/đổi quyền có hiệu lực NGAY: JWT `OnTokenValidated` hỏi DB mỗi yêu cầu, trả 401 kèm `code` (`account_locked`, `role_changed`...). Admin không tự khóa/đổi quyền mình.
- CSP đang `Enforce`: KHÔNG viết `<script>` nội tuyến, `onclick=`, `onerror=`. Ảnh lỗi dùng `data-fallback="placeholder|remove"` + bộ lắng nghe chung cuối `ui.js`. Mỗi `id` HTML phải duy nhất (ô tìm kiếm admin là `adminSearchInput` vì thanh menu đã dùng `searchInput`).
- Rate limit (`login`, `register`, `chat`) theo IP; KHÔNG tin `X-Forwarded-For` (đặt sau proxy thì phải cấu hình ForwardedHeaders).
- Cache `ICatalogCache` (60 giây): chỉ dữ liệu CÔNG KHAI (hot/latest/featured/genres/danh sách duyệt không từ khóa); không bao giờ cache dữ liệu theo người dùng. Controller admin truyện/chương/thể loại gắn `[ServiceFilter(typeof(InvalidateCatalogCacheFilter))]`.
- MySQL: đổi chỉ mục mà khóa ngoại đang dựa vào thì tạo chỉ mục mới TRƯỚC rồi mới xóa cái cũ (nếu không lỗi 1553); sửa tay migration do EF sinh sai thứ tự. Chỉ thêm chỉ mục khi có số đo (đã đo trên 400k bình luận).
- Không log mật khẩu/token/nội dung request; chặn ký tự xuống dòng trong log. Không commit secret/ảnh; ảnh bìa và ảnh thể loại do người dùng tự tìm, ghi nguồn ở `CREDITS.md`.

## 5. Đã xong
- Giai đoạn 0–8: nền tảng, ~9 bảng, JWT/BCrypt, API truyện/chương/thể loại, admin CRUD + upload, giao diện đọc và admin, theo dõi/lịch sử/bình luận, chatbot OpenRouter.
- 10.x: đánh giá sao, bình luận emoji/GIF, dữ liệu giả lập, redesign (tím đêm + xanh ngọc, dark mode), admin bình luận, trả lời + thích bình luận.
- 11.1–11.5: khóa tài khoản tức thì, giới hạn đăng nhập/đăng ký, bắt lỗi toàn cục + log + health, header bảo mật + CSP + HSTS, bộ test phân quyền.
- 12.1: chỉ mục `IX_comments_CreatedAt`, `IX_ratings_ComicId_Stars` (migration đã chạy).
- 12.2: `CatalogCache` + filter xóa cache (người dùng đã chuyển sang 12.3 nên coi là xong, chưa xác nhận bảng kiểm tra).

## 6. Đang làm dở: 12.3 nén ảnh tải lên + ảnh thu nhỏ WebP
Phần máy chủ đã viết và kiểm thử NGOÀI repo (sandbox của trợ lý trước), nên repo CHƯA có; làm lại theo đặc tả:
- Thêm NuGet `SixLabors.ImageSharp` (3.1.x). Tạo `Services/ImageOptimizer.cs`; thay `Services/FileStorage.cs` (giữ chữ ký `SaveImageAsync/DeleteLocal/IsAllowedUrl`, thêm `OptimizeFolderAsync`).
- Bìa (`folder=="covers"`): `.webp` tối đa 800x1200 q80 + `thumbs/<cùng tên>.webp` tối đa 320x480 q75. Trang truyện: rộng tối đa 1200 q80, không thumb. Không phóng to; `AutoOrient`; bỏ EXIF/GPS; GIF động lấy khung đầu; cạnh >16383px thì lưu JPEG (giới hạn WebP).
- An toàn: `Image.IdentifyAsync` trước, từ chối >30000px/cạnh hoặc >40 triệu điểm ảnh (bom giải nén). Kiểm tra file đầy đủ vì ImageSharp giải mã nới lỏng (JPEG có `FF D9` trong 16KB cuối, PNG có `IEND`, GIF kết thúc `0x3B`, WebP đủ độ dài khai báo). Ảnh hỏng → `InvalidUploadException` (400). Xử lý xong mới ghi đĩa; `DeleteLocal` xóa luôn thumb.
- `UploadController` (admin): thêm `POST /api/Upload/optimize-existing?folder=seed/covers&limit=100&sql=true`: đổi ảnh cũ sang webp (giữ file gốc), trả báo cáo hoặc SQL `UPDATE comics SET CoverUrl=...` / `chapter_images`. Tên thư mục chỉ `^[a-z0-9_-]+(/[a-z0-9_-]+)*$`, cấm `thumbs`.
- Giao diện (chưa làm): hàm toàn cục `thumbOf(url)` cuối `ui.js` (`/uploads/<dir>/<tên>.webp` → `/uploads/<dir>/thumbs/<tên>.webp`, URL khác giữ nguyên). Dùng ảnh nhỏ ở thẻ truyện (`ui.js`), lịch sử, theo dõi, kết quả tìm, bảng admin. GIỮ ảnh lớn ở `comic.js` (chi tiết), banner `home.js`, xem trước trong form admin.
- Sau đó: backfill `seed/covers` (40 bìa), chạy SQL do endpoint trả, kiểm tra tay, chạy lại `ComicApi.AuthTests` (nên thêm endpoint mới vào danh sách test).

## 7. Việc còn lại, theo ưu tiên
1. Hoàn tất 12.3 như mục 6.
2. 12.4 Trang thống kê admin: `admin/stats.html` + `js/pages/admin-stats.js` + API admin (tận dụng chỉ mục sẵn có; có thể hiện `ICatalogCache.Stats`).
3. Người dùng đã quyết định BỎ cho tới khi họ yêu cầu: 12.6 (chatbot nhớ hội thoại, gợi ý theo lịch sử đọc), Giai đoạn 13 (Docker/chạy thật), 14 (kiểm thử tải), 9 (báo cáo, vấn đáp), nút báo cáo vi phạm bình luận.

## 8. Lưu ý đã biết
- Cache 60 giây: lượt xem/điểm sao/theo dõi trong danh sách có thể cũ; cache nằm trong bộ nhớ từng tiến trình (nhiều container sẽ lệch); sửa SQL trực tiếp không xóa cache.
- Admin tìm từ khóa HIẾM trong bình luận chậm hơn sau khi có `IX_comments_CreatedAt` (ở quy mô 400k dòng); trang sâu của danh sách admin và sắp xếp "Nổi bật" chưa tối ưu (hướng: phân trang con trỏ, cột `LikeCount`).
- DB của người dùng còn chỉ mục thừa `IX_comments_ComicId` (vô hại, có thể xóa).
- JWT lưu `localStorage` (CSP giảm nhưng không loại bỏ rủi ro XSS); `style-src 'unsafe-inline'` vì có `style="width:..%"`; Google Fonts tải ngoài; HSTS không phát ra trên localhost (cố ý); Swagger không áp CSP.
- Chatbot: không có trí nhớ, đoán ý định bằng từ khóa, phụ thuộc OpenRouter; tên model nằm trong cấu hình.
- Nhiều đoạn mã ở các bước trước được dựng lại từ lịch sử hội thoại: LUÔN đọc code thật và `git log` trước khi sửa (nhất là `ComicsController`, `GenresController` đã vá cho 12.2, và `ui.js`).
- ImageSharp theo Six Labors Split License: ổn cho đồ án, kiểm tra điều khoản nếu thương mại hóa.
