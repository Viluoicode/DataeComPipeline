# Architecture Decisions & Trade-offs

> Tài liệu này giải thích **vì sao** dự án được xây như vậy — không phải *cái gì*. Mỗi quyết định gồm: bối cảnh, lựa chọn, đánh đổi, phương án đã loại, và **"ở quy mô lớn thì đổi gì"**. Đây là phần mà người phỏng vấn senior đọc để biết bạn *tư duy kỹ thuật* hay chỉ *ghép code*.
>
> Format theo kiểu ADR (Architecture Decision Record), rút gọn.

---

## ADR-1 — Tách OLTP và OLAP thành hai database

**Bối cảnh.** App vừa phải *ghi* đơn hàng nhanh (giao dịch), vừa phải *đọc* báo cáo phân tích nặng (JOIN/GROUP BY trên hàng trăm nghìn dòng). Hai nhu cầu này xung đột: index tốt cho ghi (B-tree, hẹp) thì tệ cho quét phân tích, và ngược lại.

**Quyết định.** Hai store riêng:
- **OLTP** (`dbo`, row-store, EF Core): ghi đơn, khách, sản phẩm — normalize, B-tree index, transaction.
- **OLAP** (`bronze/silver/gold`, Columnstore, Dapper): đọc phân tích — denormalize, nén theo cột.
- Đồng bộ qua **ETL** (Hangfire).

**Đánh đổi.**
- ✅ Mỗi store tối ưu cho đúng workload của nó; báo cáo nặng không làm chậm trang bán hàng.
- ✅ Đây chính là **CQRS ở mức database** — write model và read model tách biệt.
- ❌ Có **độ trễ** (eventual consistency): số liệu OLAP trễ hơn OLTP một nhịp ETL.
- ❌ Phức tạp vận hành hơn: phải có ETL, phải xử lý watermark, phải lo data quality.

**Phương án đã loại.**
- *Một DB, đánh thêm index cho báo cáo*: index phân tích làm chậm ghi + phình DB; vẫn dùng row-store nên quét chậm.
- *Read replica của OLTP*: giảm tải đọc nhưng vẫn là row-store + schema normalize → query phân tích vẫn chậm và khó viết.

**Ở quy mô lớn.** OLAP tách hẳn sang kho chuyên dụng (Synapse / BigQuery / Snowflake / ClickHouse); OLTP có thể sharding. Đồng bộ chuyển từ batch ETL sang **streaming CDC** (xem ADR-5).

---

## ADR-2 — Columnstore cho bảng fact OLAP

**Bối cảnh.** Câu hỏi phân tích thường là "SUM doanh thu theo category 90 ngày" — chạm **ít cột nhưng rất nhiều dòng**.

**Quyết định.** Bảng fact (`silver`) dùng **Clustered Columnstore Index**.

**Vì sao nhanh.**
- Lưu **theo cột** → chỉ đọc cột cần (revenue, date, category), bỏ qua phần còn lại.
- **Nén cao** (cùng kiểu dữ liệu cạnh nhau) → ít I/O.
- **Batch-mode execution** + segment elimination.

**Số liệu thực đo.** Cùng query "doanh thu theo category 90 ngày" trên ~300k dòng: **OLAP ~90ms vs OLTP (row-store, 3-way JOIN) ~1200ms → nhanh ~13×.**

**Cạm bẫy đã gặp (kể được trong phỏng vấn).** Columnstore chỉ nhanh khi rowgroup ở trạng thái **COMPRESSED**. Data mới nạp vào nằm ở **delta store** (row-mode, chậm) cho tới khi `REORGANIZE WITH COMPRESS`. Lần benchmark đầu OLAP *chậm hơn* OLTP đúng vì lý do này → phải thêm `CompressColumnstoreJob` chạy đêm.

**Đánh đổi.** ❌ Columnstore tệ cho điểm-tra-cứu (lookup 1 dòng) và update lẻ → đó là lý do nó chỉ dùng cho OLAP, không dùng cho OLTP.

**Ở quy mô lớn.** Thêm **partition theo thời gian** để switch/load nhanh và prune partition; archiving partition cũ.

---

## ADR-3 — Medallion Architecture (Bronze → Silver → Gold)

**Bối cảnh.** Cần vừa giữ dữ liệu thô (để truy vết/replay), vừa có dữ liệu sạch dạng sao (star schema), vừa có số liệu trả về tức thì cho dashboard.

**Quyết định.** Ba tầng:
- **Bronze** — raw, append-only, đúng như nguồn (audit & replay).
- **Silver** — fact + dimension đã làm sạch, star schema, Columnstore.
- **Gold** — bảng **pre-aggregated** theo từng câu hỏi nghiệp vụ (DailySalesByCategory, MonthlyTopProducts, CustomerLifetimeValue).

**Đánh đổi.**
- ✅ Tách trách nhiệm rõ ràng; lỗi tầng nào sửa tầng đó; replay được từ Bronze.
- ✅ Dashboard đọc Gold → ~5-10ms (đã tính sẵn), không JOIN runtime.
- ❌ Lưu trùng dữ liệu ở 3 tầng (đổi lấy tốc độ + khả năng truy vết — chấp nhận được).

**Phương án đã loại.** *Query thẳng fact mỗi lần dashboard load*: chậm hơn và tốn CPU lặp lại cho cùng một con số.

**Ở quy mô lớn.** Gold refresh hiện là **truncate + repopulate** (đơn giản, đủ ở quy mô này). Lớn hơn → **incremental aggregation** (chỉ tính lại ngày thay đổi) hoặc materialized view tăng tiến.

---

## ADR-4 — Slowly Changing Dimension Type 2 cho dimension

**Bối cảnh.** Khách đổi thành phố/hạng thành viên. Nếu overwrite, báo cáo lịch sử sẽ sai ("doanh thu theo thành phố năm ngoái" bị gán theo địa chỉ *hiện tại*).

**Quyết định.** Dimension dùng **SCD Type 2**: giữ lịch sử bằng `ValidFrom / ValidTo / IsCurrent / Version`, phát hiện thay đổi bằng **hash SHA-256** của các cột nghiệp vụ. Khi hash đổi: đóng bản cũ (`ValidTo`, `IsCurrent=0`) + chèn bản mới.

**Đánh đổi.**
- ✅ Báo cáo lịch sử **chính xác theo thời điểm** (point-in-time correctness).
- ✅ Hash giúp **phát hiện thay đổi rẻ** (so 1 cột thay vì so từng field).
- ❌ Dimension phình theo số lần thay đổi; query phải lọc `IsCurrent=1` khi cần bản mới nhất.

**Phương án đã loại.** *SCD Type 1 (overwrite)*: đơn giản nhưng mất lịch sử → sai báo cáo theo thời gian.

**Cạm bẫy đã gặp.** SQL Server biên dịch cả batch trước khi chạy → `ALTER TABLE ADD column` rồi tham chiếu cột đó *trong cùng batch* sẽ lỗi compile (`Invalid column name`). Phải khai báo cột (`RowHash`) ngay trong `CREATE TABLE`.

**Ở quy mô lớn.** Dùng `MERGE` có `HOLDLOCK` (đã làm) để upsert an toàn; cân nhắc SCD Type 4 (bảng history riêng) nếu dimension thay đổi quá thường xuyên.

---

## ADR-5 — ETL tăng tiến bằng Watermark (không full-reload, chưa CDC)

**Bối cảnh.** Mỗi lần ETL không nên đọc lại toàn bộ OLTP.

**Quyết định.** Lưu **watermark** = `OrderItemId` cuối đã xử lý trong `etl.Watermark`. Mỗi lần chỉ trích **delta** (`Id > watermark`), nạp theo lô 5000 bằng `SqlBulkCopy` trong một transaction, rồi tiến watermark.

**Đánh đổi.**
- ✅ Đơn giản, không cần bật tính năng DB đặc biệt; idempotent; rẻ.
- ✅ `SqlBulkCopy` nhanh hơn nhiều so với INSERT từng dòng.
- ❌ **Chỉ bắt được INSERT**, không bắt UPDATE/DELETE ở OLTP (đủ với fact append-only như order item, nhưng là giới hạn thật).
- ❌ Watermark dựa trên cột tăng dần đơn điệu — phải đảm bảo điều đó đúng.

**Phương án đã loại.**
- *Full reload mỗi lần*: đơn giản nhưng tốn kém tuyến tính theo kích thước DB.
- *CDC / Change Tracking ngay từ đầu*: mạnh hơn (bắt cả update/delete) nhưng phức tạp hơn nhiều cho một dự án demo → để dành cho roadmap.

**Ở quy mô lớn.** Chuyển sang **CDC** (SQL Server Change Data Capture) hoặc **Debezium → Kafka** để stream cả update/delete, near-real-time, thay cho batch watermark.

---

## ADR-6 — EF Core cho ghi, Dapper cho đọc

**Bối cảnh.** Hai đường dữ liệu có nhu cầu khác nhau.

**Quyết định.**
- **EF Core** (OLTP, ghi): change tracking, migration, quan hệ entity, validation → năng suất cao cho domain logic.
- **Dapper** (OLAP, đọc): map raw SQL → DTO, không overhead tracking → nhanh và kiểm soát SQL tuyệt đối cho query phân tích/Columnstore.

**Đánh đổi.** ✅ Mỗi tool làm đúng việc nó giỏi. ❌ Hai mô hình truy cập dữ liệu trong một codebase (chấp nhận được vì ranh giới rõ: ghi = EF, đọc phân tích = Dapper).

**Cạm bẫy đã gặp.** Dapper truyền transaction qua **named arg** `transaction: tx` — tham số vị trí thứ 3, KHÔNG phải thứ 2 (thứ 2 là `param`). Truyền nhầm → lỗi runtime "BeginExecuteNonQuery requires transaction".

---

## ADR-7 — AI Data Analyst: Text-to-SQL với phòng thủ nhiều lớp, fail-closed

**Bối cảnh.** Cho người dùng hỏi dữ liệu bằng ngôn ngữ tự nhiên → sinh SQL chạy thật. LLM **không đáng tin** (có thể sinh `DROP`, query sai bảng, prompt injection).

**Quyết định.** 4 lớp phòng thủ, **fail-closed** (không qua được validate thì KHÔNG chạy):
1. **Prompt** chỉ mô tả đúng các bảng/cột trong whitelist (`schema.ecommerce.json` là *single source of truth* cho cả prompt lẫn validator).
2. **AST validator** (ScriptDom — parser T-SQL thật, không regex): bắt buộc 1 câu `SELECT` duy nhất, bảng/cột phải trong whitelist, cấm `INTO`/`OPENROWSET`/cross-DB, tự chèn `TOP` để chặn result khổng lồ.
3. **Tài khoản DB least-privilege** `analyst_ro`: chỉ `SELECT` trên `gold`, deny mọi write/DDL. **Đây là chốt chặn thật** — kể cả validator có bug, DB cũng từ chối ghi.
4. **Resource guard**: command timeout + giới hạn số dòng đọc.

**Vì sao AST chứ không regex.** Regex dễ bị bypass (comment, lồng query, biến thể cú pháp). Parse ra **cây cú pháp** rồi duyệt node mới chắc chắn "đây đúng là 1 SELECT, chạm đúng các bảng này".

**Vì sao Microsoft.Extensions.AI + provider Offline mặc định.** `IChatClient` là abstraction → đổi nhà cung cấp (Azure OpenAI / OpenAI / Ollama) chỉ là thay đăng ký DI, không sửa logic. Provider **Offline** (canned SQL, zero API key) đảm bảo toàn bộ pipeline + test + eval chạy được mà không tốn tiền/khóa.

**Quyết định tích hợp.** Phần này được tích hợp vào pipeline như **một service riêng** (`ai-analyst/`, .NET 10) đọc tầng Gold, rồi **proxy qua API .NET 9** (`POST /api/ask`, chỉ Admin/Staff) — UI chat nằm trong React Admin. Lý do proxy thay vì gọi thẳng từ browser: analyst **không lộ ra ngoài**, thừa hưởng JWT + correlation-id của API, và **không phải viết lại** lớp an toàn đã có.

**Cạm bẫy đã gặp.** Provider Offline ban đầu hardcode canned SQL cho schema demo F&B → mọi câu hỏi e-commerce bị validator từ chối (đúng theo thiết kế an toàn). Fix: cho Offline **đọc `fewShot` từ schema đang load** → hoạt động với bất kỳ schema nào, vẫn zero-key.

**Ở quy mô lớn.** Thêm cache câu hỏi→SQL; thêm tầng "clarify" khi câu hỏi mơ hồ; eval harness gắn vào CI để chặn regression độ chính xác; mở rộng whitelist sang Silver star schema khi cần câu hỏi sâu hơn.

---

## ADR-8 — Hangfire cho background jobs (không hosted service tay)

**Quyết định.** ETL, data-quality, nén Columnstore chạy qua **Hangfire** (storage SQL).

**Đánh đổi.** ✅ Có dashboard, retry, lịch cron cấu hình được, `DisableConcurrentExecution` chống chạy chồng. ✅ Job bền qua restart (lưu ở SQL). ❌ Thêm 1 DB schema + dependency.

**Phương án đã loại.** *`IHostedService` + `Timer` tự viết*: nhẹ hơn nhưng tự lo retry/lịch/quan sát/chống chồng → tốn công và dễ sai.

**Ở quy mô lớn.** Tách worker ra process/máy riêng (Hangfire server riêng) để job nặng không tranh tài nguyên với web; hoặc chuyển sang message queue + worker.

---

## ADR-9 — JWT access + refresh token, role-based

**Quyết định.** Access token ngắn hạn (60′) + refresh token dài hạn có **xoay vòng** (rotation); mật khẩu hash **BCrypt**; phân quyền theo role (Admin/Staff/Customer).

**Đánh đổi.** ✅ Stateless, scale ngang dễ (không session server). ✅ Access lộ thì hết hạn nhanh; refresh xoay vòng giảm rủi ro tái dùng. ❌ Thu hồi access token tức thì khó (bản chất JWT) → bù bằng TTL ngắn.

**Ở quy mô lớn.** Refresh token lưu kèm cờ thu hồi; cân nhắc IdentityServer/Azure AD B2C thay vì tự quản.

---

## ADR-10 — Clean Architecture (Domain / Application / Infrastructure / Api)

**Quyết định.** Phân tầng với chiều phụ thuộc hướng vào trong: Domain không phụ thuộc ai; Infrastructure/Api phụ thuộc abstraction ở trong.

**Đánh đổi.** ✅ Business logic tách khỏi framework/DB → **test được không cần DB** (đã có 48 test với EF InMemory + Moq). ✅ Đổi hạ tầng (DB, provider AI) không đụng domain. ❌ Nhiều project/abstraction hơn → với CRUD nhỏ là over-engineering, nhưng với pipeline nhiều tầng thì đáng.

**Thành thật.** Đây là Clean Architecture *thực dụng* (kiểu Jason Taylor), không giáo điều — không tạo interface cho mọi thứ chỉ để có interface.

---

## Bảng tổng "nếu scale thì đổi gì" (câu hỏi tủ khi phỏng vấn)

| Thành phần | Hiện tại (đủ cho demo) | Ở 100M+ rows / production |
|---|---|---|
| Đồng bộ OLTP→OLAP | Watermark batch (chỉ INSERT) | CDC / Debezium + Kafka (cả update/delete, near-real-time) |
| OLAP store | SQL Server Columnstore | Synapse / ClickHouse / Snowflake + **partition theo thời gian** |
| Gold refresh | Truncate + repopulate | Incremental aggregation (chỉ tính lại delta) |
| Jobs | Hangfire chung process | Worker tách riêng / message queue |
| AI accuracy | Eval thủ công | Eval gate trong CI + cache + clarify層 |
| Auth | Tự quản JWT | IdentityServer / Azure AD B2C |

---

## Production Roadmap (P0 / P1 / P2)

Khoảng cách giữa *portfolio* và *production*, ưu tiên rõ ràng. **P0 = bắt buộc trước khi lên prod.**

### 🔴 P0 — bắt buộc

| Hạng mục | Trạng thái |
|---|---|
| **Rate-limit `/api/ask`** (per-user, 15/phút, HTTP 429) — chặn lạm dụng + chi phí LLM | ✅ **Đã làm** (`AddRateLimiter` + `RequireRateLimiting("ai-ask")`) |
| **Cache câu hỏi→kết quả** (IMemoryCache, TTL 10′) — bỏ gọi LLM lặp | ✅ **Đã làm** |
| **Audit log AI** (ai hỏi gì → status → latency, kèm correlation-id) | ✅ **Đã làm** |
| **Secrets fail-fast** — Production từ chối boot nếu `Jwt:Secret` là dev-default/yếu | ✅ **Đã làm** |
| **ETL bắt UPDATE/DELETE** — watermark hiện chỉ bắt INSERT | ⏳ Cần CDC (xem ADR-5) |
| **OLAP migration tooling** — thay `IF NOT EXISTS` bằng versioned (DbUp/Flyway) | ⏳ |
| **PII guard cho AI** — Gold chỉ aggregate, mask dữ liệu cá nhân | ⏳ (schema Gold hiện đã hầu như không chứa PII) |
| **HTTPS/CORS chặt** | ⏳ (deployment) |

> **Vì sao 4 cái đầu làm trước:** chúng bảo vệ tính năng AI — phần *đắt tiền* (mỗi câu hỏi = 1 lần gọi LLM) và *dễ bị lạm dụng* nhất. Đây cũng là defense-in-depth mở rộng: ADR-7 chống *SQL độc*, còn rate-limit/cache/audit chống *lạm dụng tài nguyên & thiếu truy vết*.

### 🟡 P1 — để chạy ổn ở quy mô thật
Partition fact theo thời gian · Gold incremental (bỏ truncate) · tách worker ETL · observability backend bền + metrics/alerting · **AI eval-gate trong CI** · AI metrics (refusal rate, accuracy, cost) · backup/DR · load test.

### 🟢 P2 — tính năng sản phẩm
Multi-tenancy (per-tenant `analyst_ro`) · AI clarify + feedback loop (👍/👎) · AI streaming qua SignalR · scheduled reports/export · alerting nghiệp vụ · RBAC chi tiết · mở rộng dimension · saved questions/dashboard builder.
