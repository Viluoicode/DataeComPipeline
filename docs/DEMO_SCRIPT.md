# Demo Script (2 phút) + README Polish Checklist

> Mục tiêu: recruiter/interviewer xem **2 phút** là nắm được "người này làm thật, hiểu thật". Quay màn hình hoặc demo live đều theo kịch bản này. Nói **ngắn, có số liệu**, để UI tự nói thay.

---

## Phần 1 — Kịch bản demo 2 phút (có canh giờ)

> Chuẩn bị trước: 3 service đang chạy (API :5193, analyst :8090, frontend :5173) HOẶC `docker compose up -d`. Đăng nhập sẵn nếu quay video để tiết kiệm giây.

### [0:00–0:15] Mở đầu — nói "đây là cái gì" (đừng để màn hình trống)
> *"Đây là ECommerPipeline — một e-commerce full-stack minh hoạ pipeline dữ liệu OLTP sang OLAP, có thêm một AI Data Analyst hỏi-đáp bằng ngôn ngữ tự nhiên. Tôi sẽ demo nhanh luồng end-to-end."*
- Màn hình: trang storefront (`/`).

### [0:15–0:40] Storefront → tạo đơn (chứng minh OLTP ghi)
- Vào Shop → chọn 1 sản phẩm → Add to cart → Checkout → đặt đơn.
> *"Đơn này vừa ghi vào database giao dịch OLTP qua EF Core."*
- Nhanh, không lề mề.

### [0:40–1:05] Admin Dashboard (chứng minh OLAP đọc + real-time)
- Đăng nhập `admin@ecom.com` → `/admin`.
> *"Dashboard này đọc số liệu pre-aggregated từ tầng Gold của kiến trúc Medallion — khoảng 5-10ms vì số đã được ETL tính sẵn, không JOIN runtime. Cập nhật real-time qua SignalR khi ETL chạy xong."*
- Chỉ vào 1 KPI + 1 biểu đồ.

### [1:05–1:15] (tuỳ chọn) Trigger ETL — chứng minh pipeline động
- Stress Test → Trigger ETL → quay lại Dashboard thấy số đổi.
> *"ETL tăng tiến theo watermark, chỉ xử lý delta, nạp Bronze→Silver→Gold."*

### [1:15–1:50] ⭐ AI Data Analyst (phần khác biệt — dành nhiều thời gian nhất)
- Sidebar → **Ask Data (AI)** → bấm gợi ý *"Which customers have the highest lifetime value?"*
> *"Câu hỏi tiếng Việt/Anh được chuyển thành SQL. Nhưng điểm quan trọng là lớp an toàn: SQL được parse thành cây cú pháp, kiểm tra chỉ-SELECT và whitelist bảng, rồi chạy bằng một tài khoản DB chỉ có quyền đọc tầng Gold."*
- Bấm **"Xem SQL đã sinh"** → cho thấy SQL thật.
> *"Đây là SQL nó thực sự chạy — minh bạch hoàn toàn."*
- (Nếu muốn ghi điểm) hỏi 1 câu **ngoài phạm vi** → cho thấy nó **Refused**:
> *"Câu này bị từ chối vì chạm bảng ngoài whitelist — đó là tính năng an toàn fail-closed đang hoạt động, không phải lỗi."*

### [1:50–2:00] Kết — nói trọng tâm kỹ thuật
> *"Điểm nhấn của dự án là tách read/write như CQRS để báo cáo không làm chậm giao dịch — đo được nhanh ~13× — và lớp phòng thủ nhiều tầng quanh LLM. Chi tiết quyết định kiến trúc tôi ghi trong DECISIONS.md."*

---

### Mẹo quay/diễn
- **Đừng đọc code trong demo.** Demo là để thấy nó *chạy*; code để dành lúc hỏi sâu.
- Nếu quay GIF cho README: chỉ lấy đoạn **Ask Data** (1:15–1:50) — ấn tượng nhất, ~15-20s.
- Có **backup**: nếu live demo lỗi, có sẵn video/GIF. (Bạn vừa thấy hạ tầng hay trở chứng — đừng để rủi ro live làm hỏng buổi PV.)
- Tắt notification, zoom trình duyệt ~110% cho chữ to dễ nhìn.

---

## Phần 2 — README Polish Checklist

> README là thứ recruiter đọc **30 giây đầu**. Mục tiêu: hiểu ngay dự án làm gì + ấn tượng + biết cách chạy.

### Phần đầu (above the fold — quan trọng nhất)
- [ ] **1 dòng tagline** rõ nghĩa ngay dưới tên (đã có).
- [ ] **Badges**: build CI, .NET version, license (đã có) — cân nhắc thêm badge "tests passing".
- [ ] **1 ảnh/GIF hero** ngay đầu: ưu tiên **GIF demo Ask Data** hoặc ảnh Dashboard. *(Hiện README chưa có ảnh — đây là việc nên làm nhất.)*
- [ ] Sơ đồ kiến trúc ASCII (đã có) — giữ, nó tốt.

### Nội dung
- [ ] **Quick Start** chạy được bằng copy-paste (`docker compose up -d`) (đã có).
- [ ] **Demo accounts** dạng bảng (đã có).
- [ ] **Bảng Performance** với số thật ~90ms vs ~1200ms (đã có) — đây là điểm nhấn, để nổi bật.
- [ ] **Features** có mục AI Data Analyst (đã có).
- [ ] **Tech stack** liệt kê version (đã có).
- [ ] Link tới `docs/DECISIONS.md`, `docs/AI_ANALYST_INTEGRATION.md`, `docs/ARCHITECTURE.md` (đã có phần Documentation).

### Cần bổ sung
- [ ] **Ảnh chụp màn hình** (3-4 tấm): Storefront, Admin Dashboard, **Ask Data (AI)**, Jaeger tracing. Đặt trong `docs/images/` và nhúng vào README.
- [ ] GIF demo 15-20s (dùng ScreenToGif / ShareX trên Windows).
- [ ] Mục **"What I learned / Key decisions"** ngắn (3-4 bullet) link tới DECISIONS.md — cho thấy chiều sâu.
- [ ] Kiểm tra mọi link nội bộ không vỡ.
- [ ] GitHub repo: thêm **About** + **Topics** (`dotnet`, `data-engineering`, `etl`, `olap`, `react`, `llm`, `text-to-sql`, `clean-architecture`) + ghim repo lên profile.

### Cách chụp ảnh nhanh (Windows)
- Ảnh tĩnh: `Win + Shift + S` (Snipping Tool).
- GIF: **ScreenToGif** (free) — quay vùng Ask Data, export GIF < 5MB để README load nhanh.
- Lưu vào `docs/images/`, nhúng: `![Ask Data](docs/images/ask-data.gif)`.

---

## Thứ tự ưu tiên (nếu ít thời gian)
1. **GIF demo Ask Data** vào đầu README ← ấn tượng nhất, làm trước.
2. **3-4 ảnh screenshot** vào README.
3. Đọc kỹ `DECISIONS.md` + `INTERVIEW_PREP.md` (quan trọng hơn cả ảnh — vì quyết định ở phòng phỏng vấn).
4. GitHub topics + About + pin repo.
5. (Nếu có thời gian) video 2 phút theo kịch bản trên, upload YouTube unlisted, link trong README.
