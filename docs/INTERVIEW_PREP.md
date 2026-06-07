# Interview Prep — ECommerPipeline

> Bộ câu hỏi + câu trả lời mẫu cho đúng stack của dự án. Mỗi câu: **ý chính cần nói** (gạch đầu dòng để bạn nhớ, không học thuộc lòng từng chữ). Trả lời ngắn gọn, có **số liệu** và **trade-off** → đó là dấu hiệu của người *hiểu*, không phải người *đọc thuộc*.
>
> 🎯 Nguyên tắc vàng: mỗi câu trả lời nên có **(1) quyết định + (2) vì sao + (3) đánh đổi hoặc "nếu scale thì..."**. Người phỏng vấn không tìm đáp án đúng — họ tìm cách bạn *suy nghĩ*.

---

## A. Kiến trúc & Data Engineering (phần lõi — hỏi nhiều nhất)

**Q1. Giới thiệu dự án trong 60 giây.**
- "Một e-commerce full-stack minh hoạ pipeline dữ liệu **OLTP → ETL → OLAP**. Đơn hàng ghi vào DB giao dịch (EF Core), một job ETL tăng tiến (Hangfire) đồng bộ sang DB phân tích Columnstore theo **kiến trúc Medallion** (Bronze/Silver/Gold) với **SCD Type 2**. Dashboard admin React đọc số liệu pre-aggregated real-time qua SignalR."
- "Trên đó tôi gắn một **AI Data Analyst**: hỏi bằng tiếng Việt/Anh → sinh SQL an toàn (chỉ-đọc) trên tầng Gold."
- "Điểm nhấn kỹ thuật là **tách read/write** và **lớp an toàn quanh LLM**."

**Q2. Vì sao tách OLTP và OLAP? Không gộp một DB cho gọn à?**
- Hai workload xung đột: ghi giao dịch cần index hẹp (B-tree); đọc phân tích quét nhiều dòng ít cột cần Columnstore.
- Gộp lại: báo cáo nặng làm chậm trang bán hàng; index phân tích làm chậm ghi.
- "Đây thực ra là **CQRS ở mức database** — write model và read model riêng."
- Đánh đổi: có độ trễ ETL (eventual consistency) — chấp nhận được vì báo cáo không cần realtime tuyệt đối.

**Q3. Columnstore là gì, vì sao nhanh hơn cho phân tích?**
- Lưu **theo cột** thay vì theo dòng → query chỉ đọc cột cần, bỏ qua phần còn lại.
- Nén cao (cùng kiểu dữ liệu kề nhau) + batch-mode execution.
- **Số liệu của tôi: cùng query trên 300k dòng, OLAP ~90ms vs OLTP ~1200ms — nhanh ~13×.**
- Bẫy: chỉ nhanh khi rowgroup **COMPRESSED**; data mới nằm ở delta store (chậm) tới khi nén → tôi có job nén Columnstore chạy đêm.

**Q4. Medallion là gì? Vì sao 3 tầng mà không 1?**
- Bronze = raw (audit/replay), Silver = sạch + star schema (Columnstore fact + dimension), Gold = pre-aggregated theo câu hỏi nghiệp vụ.
- Tách trách nhiệm: lỗi tầng nào sửa tầng đó, replay được từ Bronze.
- Dashboard đọc Gold ~5-10ms vì số đã tính sẵn, không JOIN runtime.
- Đánh đổi: lưu trùng — đổi lấy tốc độ + truy vết.

**Q5. SCD Type 2 là gì? Vì sao cần?**
- Giữ **lịch sử thay đổi** của dimension (khách đổi thành phố/hạng): `ValidFrom/ValidTo/IsCurrent/Version`.
- Phát hiện đổi bằng **hash SHA-256** các cột nghiệp vụ → đổi thì đóng bản cũ + chèn bản mới.
- Vì sao cần: nếu overwrite (Type 1), báo cáo lịch sử sẽ sai — "doanh thu theo thành phố năm ngoái" bị gán theo địa chỉ hiện tại. Type 2 cho **point-in-time correctness**.

**Q6. ETL của bạn hoạt động thế nào? Tăng tiến ra sao?**
- Đọc **watermark** (Id cuối đã xử lý) → chỉ trích delta (`Id > watermark`).
- Upsert dimension SCD2 → nạp Bronze + Silver theo lô 5000 bằng `SqlBulkCopy` trong 1 transaction → tiến watermark → refresh Gold → bắn event SignalR.
- Giới hạn thành thật: watermark **chỉ bắt INSERT**, không bắt update/delete (đủ cho fact append-only).

**Q7. (Tủ) Nếu dữ liệu lên 100 triệu dòng / cần realtime thì đổi gì?**
- Đồng bộ: bỏ watermark batch → **CDC / Debezium + Kafka** (bắt cả update/delete, near-realtime).
- OLAP: **partition theo thời gian** + có thể chuyển kho chuyên dụng (ClickHouse/Snowflake).
- Gold: từ truncate+repopulate → **incremental aggregation** (chỉ tính lại ngày thay đổi).
- Jobs: tách worker riêng để không tranh tài nguyên với web.

**Q8. Eventual consistency — nếu sếp hỏi "sao số dashboard khác số đơn hàng"?**
- Vì OLAP trễ một nhịp ETL so với OLTP. Đó là đánh đổi có chủ đích để báo cáo không làm chậm giao dịch.
- Kiểm soát bằng: hiển thị "cập nhật lúc...", chạy ETL đủ thường xuyên, và **data-quality job** đối chiếu tổng OLTP vs OLAP.

---

## B. Backend / .NET

**Q9. Vì sao dùng cả EF Core lẫn Dapper?**
- EF Core cho **ghi** (OLTP): change tracking, migration, validation, domain logic → năng suất.
- Dapper cho **đọc phân tích** (OLAP): raw SQL, không tracking overhead → nhanh + kiểm soát SQL tuyệt đối cho Columnstore.
- "Mỗi tool làm đúng việc nó giỏi; ranh giới rõ nên không lẫn lộn."

**Q10. Clean Architecture trong dự án thể hiện ở đâu?**
- 4 tầng: Domain (thuần, không phụ thuộc) / Application (use case, interface) / Infrastructure (EF, Dapper, ETL, AI) / Api.
- Lợi ích cụ thể: **test business logic không cần DB** — 48 test với EF InMemory + Moq.
- Thành thật: tôi làm *thực dụng*, không tạo interface cho mọi thứ chỉ để có interface.

**Q11. Background job xử lý sao? Vì sao Hangfire?**
- Hangfire (storage SQL): dashboard, retry, cron cấu hình, `DisableConcurrentExecution` chống chạy chồng, job bền qua restart.
- Thay vì tự viết `IHostedService`+`Timer` thì phải tự lo retry/lịch/quan sát.

**Q12. Auth hoạt động thế nào?**
- JWT **access (ngắn) + refresh (xoay vòng)**; mật khẩu **BCrypt**; phân quyền role.
- Stateless → scale ngang dễ. Access lộ thì hết hạn nhanh; refresh rotation giảm rủi ro tái dùng.
- Hạn chế: thu hồi access tức thì khó → bù bằng TTL ngắn.

**Q13. SignalR dùng làm gì?**
- Đẩy event real-time: ETL xong → dashboard tự cập nhật KPI, không cần F5.
- Sau này tái dùng cùng kênh cho tính năng khác (vd stream câu trả lời AI).

---

## C. Tính năng AI (điểm khác biệt — chuẩn bị kỹ)

**Q14. AI Data Analyst làm gì? Có chỉ là gọi ChatGPT không?**
- Biến câu hỏi NL → SQL **chạy thật** trên tầng Gold, trả về số + diễn giải.
- "**Giá trị không nằm ở việc gọi LLM** — mà ở **lớp an toàn/đúng đắn** quanh nó. LLM không đáng tin, nên mọi SQL nó sinh đều phải qua kiểm duyệt trước khi chạy."

**Q15. Làm sao chặn AI sinh SQL độc (DROP TABLE, đọc bảng cấm, injection)?**
- **4 lớp phòng thủ, fail-closed** (không qua validate thì không chạy):
  1. Prompt chỉ kể bảng/cột trong whitelist.
  2. **AST validator (ScriptDom)**: 1 câu SELECT duy nhất, whitelist bảng/cột, cấm INTO/OPENROWSET/cross-DB, tự chèn TOP.
  3. **Tài khoản DB `analyst_ro`**: chỉ SELECT trên gold, deny mọi write/DDL — chốt chặn thật kể cả validator có bug.
  4. Timeout + giới hạn dòng.

**Q16. Vì sao validate bằng AST chứ không regex?**
- Regex dễ bị bypass: comment, lồng query, biến thể cú pháp.
- Parse ra **cây cú pháp** rồi duyệt node → chắc chắn "đúng là 1 SELECT, chạm đúng các bảng này". Đây là cách làm đúng đắn, không phải chắp vá.

**Q17. Nếu LLM sinh SQL sai bảng thì sao?**
- Validator **từ chối** (fail-closed) → trả về trạng thái "Refused" kèm lý do, KHÔNG chạy gì cả.
- "Việc nó từ chối câu hỏi ngoài phạm vi là **tính năng an toàn đang hoạt động**, không phải bug."

**Q18. Không có API key thì AI chạy kiểu gì?**
- Có provider **Offline** (deterministic, zero key) để toàn bộ pipeline + test + eval chạy được miễn phí.
- Đổi sang OpenAI/Azure/Ollama chỉ là thay đăng ký DI (`IChatClient`), không sửa logic.
- Tôi đã sửa Offline để **đọc fewShot từ schema đang load** → hoạt động với bất kỳ schema nào.

**Q19. Vì sao trỏ AI vào Gold mà không phải dữ liệu thô?**
- Gold đã denormalize, sạch, tên cột rõ, một schema → là đích NL→SQL lý tưởng (không phải JOIN surrogate key của SCD2).
- Câu hỏi về dữ liệu không có trong Gold sẽ bị từ chối đúng — mở rộng sau bằng cách thêm Silver vào whitelist.

**Q20. Vì sao proxy AI qua API .NET 9 thay vì gọi thẳng từ browser?**
- Để analyst **không lộ ra ngoài** (không CORS), và thừa hưởng JWT + correlation-id của API.
- Tái dùng lớp an toàn đã có thay vì viết lại.

---

## D. DevOps / Testing / Chất lượng

**Q21. Test những gì? Bao nhiêu?**
- 48 test pipeline (xUnit + Moq + FluentAssertions + EF InMemory) tập trung **business logic** (ETL, SCD2 hash, report).
- 27 test cho AI analyst, trọng tâm là **SqlValidator** (chính là lớp an toàn).
- Triết lý: test cái *quan trọng và dễ sai*, không chạy theo % coverage.

**Q22. CI/CD?**
- GitHub Actions: restore → build → test mỗi push. Có Dockerfile + docker-compose chạy cả stack một lệnh.

**Q23. Quan sát hệ thống (observability)?**
- **OpenTelemetry → Jaeger** (distributed tracing: request → DB → ETL).
- **Serilog** structured JSON + **Correlation ID** xuyên suốt 1 request để debug.
- **Data-quality job** 11 kiểm tra (đối chiếu tổng, null, trùng...).

**Q24. Data quality kiểm gì?**
- Đối chiếu tổng OLTP vs OLAP, kiểm null ở cột bắt buộc, trùng key, tính hợp lệ ngày... ghi kết quả vào `dq.TestResults` để theo dõi.

---

## E. War stories — "kể về một bug bạn từng sửa" (RẤT ăn điểm)

> Đây là phần AI **không** thể cho ứng viên khác. Kể có cấu trúc: **triệu chứng → chẩn đoán → nguyên nhân gốc → cách sửa → bài học.**

**War story 1 — Columnstore lúc đầu *chậm hơn* row-store.**
- Triệu chứng: benchmark OLAP ~chậm hơn OLTP, ngược kỳ vọng.
- Chẩn đoán: kiểm tra trạng thái rowgroup → phần lớn ở **delta store** (OPEN), chưa COMPRESSED.
- Nguyên nhân: data mới nạp chưa nén → chạy ở row-mode.
- Sửa: `REORGANIZE WITH COMPRESS` + thêm job nén đêm. Sau đó OLAP nhanh ~13×.
- Bài học: Columnstore chỉ phát huy khi rowgroup đã nén — phải chủ động quản lý.

**War story 2 — `Invalid column name 'IsCurrent'` (schema drift + SQL batch).**
- Triệu chứng: app chết khi tạo OLAP schema.
- Nguyên nhân kép: (1) OLAP DB cũ thiếu cột SCD2, mà script `IF NOT EXISTS` nên không ALTER bảng cũ; (2) SQL Server compile cả batch trước khi chạy nên `ALTER ADD column` rồi dùng ngay trong cùng batch sẽ lỗi.
- Sửa: khai báo cột ngay trong `CREATE TABLE`; với DB cũ thì tạo lại sạch.
- Bài học: hiểu *cơ chế compile-cả-batch* của SQL Server + cạm bẫy migration idempotent.

**War story 3 — AI luôn "Refused" với dữ liệu e-commerce.**
- Triệu chứng: mọi câu hỏi đều bị từ chối, validator báo "table gold.DimProduct not allowed".
- Chẩn đoán: provider Offline hardcode canned SQL cho schema demo F&B → sinh SQL trỏ bảng không có trong whitelist e-commerce → validator chặn (đúng thiết kế).
- Sửa: cho Offline **đọc fewShot từ schema đang load**.
- Bài học: phân biệt "lớp an toàn hoạt động đúng" với "bug" — và giữ `schema.json` là single source of truth.

**War story 4 — Docker treo + Dapper transaction.**
- Docker Desktop crash do thiếu RAM → pivot sang chạy local; `wslrelay` còn giữ port phải kill.
- Dapper: transaction phải truyền qua **named arg** `transaction: tx` (vị trí 3), truyền nhầm vị trí 2 (param) → lỗi runtime.
- Bài học: đọc kỹ chữ ký API + biết chẩn đoán hạ tầng (port, process, RAM), không chỉ code.

---

## F. Câu "khó" / câu bẫy

**Q25. "Thời nay AI sinh được dự án này trong 1-2 ngày, sao vẫn quan trọng?"**
- "Đúng là *scaffold code* nhanh — và tôi cũng dùng AI để tăng tốc. Nhưng cái phỏng vấn đang test không phải 'gõ ra được code' mà là 'hiểu vì sao + sửa được khi vỡ'."
- "Ví dụ tôi giải thích được vì sao Columnstore chậm lúc đầu, vì sao validate bằng AST chứ không regex, nếu scale lên 100M dòng thì đổi watermark sang CDC. AI không trả lời thay tôi những cái đó trong phòng phỏng vấn."
- Tự tin, không phòng thủ. Biến điểm yếu thành điểm mạnh: "AI là công cụ, giá trị của tôi là quyết định kiến trúc và debug."

**Q26. "Điểm yếu lớn nhất của dự án?"**
- Trả lời thật + có hướng: "Đây là portfolio, chưa có user thật và load thật. Watermark chỉ bắt INSERT. Gold refresh kiểu truncate đủ ở quy mô này nhưng không tăng tiến. Tôi biết rõ và đã ghi hướng nâng cấp trong DECISIONS.md."
- Nói được điểm yếu = dấu hiệu hiểu sâu, không phải điểm trừ.

**Q27. "Nếu làm lại, bạn đổi gì?"**
- Đi sâu CDC ngay từ đầu thay vì watermark; tách worker ETL khỏi web; thêm eval-gate cho AI vào CI; partition fact.

**Q28. "Phần nào bạn TỰ HÀO nhất?"**
- Chọn 1 thứ và nói sâu: thường là **lớp an toàn của AI** (defense-in-depth, fail-closed, least-privilege) hoặc **Medallion + SCD2**. Tránh trả lời chung chung.

---

## Checklist trước buổi phỏng vấn

- [ ] Vẽ được sơ đồ kiến trúc trên giấy trong 2 phút (OLTP → ETL → Medallion → Dashboard + AI).
- [ ] Thuộc 3 con số: ~90ms vs ~1200ms (~13×), 5000 rows/lô, 48+27 test.
- [ ] Kể trôi 2 war story (chọn #1 và #3 hoặc #2).
- [ ] Giải thích được 4 lớp an toàn của AI mà không nhìn note.
- [ ] Có sẵn 1 câu cho "AI làm được trong 1-2 ngày" (Q25).
- [ ] Mở sẵn repo + demo chạy được (hoặc video) để screen-share.
