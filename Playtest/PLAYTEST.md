# Playtest: Tank Objective Match (prototype)

Bản này kiểm tra một giả thuyết: **khi kill không quyết định thắng, người chơi có liên tục phải chọn giữa "bắn" và "đi giữ/cướp cờ" không, và việc chọn đó có vui không.** Đây là prototype, chưa phải sản phẩm: art, âm thanh, UI đều là placeholder.

## Chạy game
- Máy: **Mac Apple Silicon** (M1 trở lên). Intel Mac không chạy được.
- Giải nén `Tank_Playtest_macOS_arm64.zip`, mở `Tank_Playtest.app`.
- Lần đầu macOS có thể chặn vì app chưa ký: bấm chuột phải vào app, chọn **Open**, hoặc chạy `xattr -cr Tank_Playtest.app` trong Terminal.
- Cần có chuột và bàn phím.

## Điều khiển
| Phím | Tác dụng |
|---|---|
| WASD hoặc mũi tên | Di chuyển |
| Chuột | Ngắm nòng (nòng quay độc lập với thân xe) |
| Chuột trái | Bắn |
| R | Reload |
| Space hoặc Shift | Dash (cooldown 6 s) |
| 1 / 2 / 3 | Chọn lõi (khi hiện bảng chọn) |
| Tab (giữ) | Bảng điểm |
| F5 | Chơi lại |
| M | Đổi chế độ (2v2, 3v2, 3 đội, solo 5) |

## Luật trận
- Trận chia **4 phase** (khoảng 60 s mỗi phase). **Đầu mỗi phase** bạn nhận **3 lõi (core) ngẫu nhiên** và chọn 1 để build tank (phím **1 / 2 / 3** hoặc bấm chuột; trận tạm dừng trong lúc chọn, tối đa 15 s). Lõi cộng dồn qua các phase.
- **Phase cuối (FINAL): điểm x2 và nhịp nhanh hơn** (cướp cờ nhanh hơn, hồi sinh 1.5 s, vật phẩm hồi nhanh gấp đôi).
- Bản đồ có **5 cờ** (A–E). Đứng một mình trong vòng tròn để cướp; có địch cùng đứng trong vòng thì cờ **bị đóng băng** (contested).
- **Cờ không có người giữ sẽ yếu dần** và về trung lập sau khoảng 45 s, nên phải có người ở lại bảo vệ hoặc quay lại gia cố. Cờ đã mất có thể bị chiếm lại.
- **Điểm số quyết định thắng thua.** Mỗi cờ đang giữ **sinh điểm mỗi giây** cho cả đội; ngoài ra có điểm capture, kill, assist, defense, contest. Kết thúc 4 phase, **đội/người có tổng điểm cao nhất thắng**.
- Chết thì hồi sinh sau 3 s (nhanh hơn ở các phase sau).
- **Vật phẩm xuất hiện ngẫu nhiên** (loại và vị trí thay đổi mỗi lần): Repair, Shield, Speed, Damage, và vũ khí đặc biệt (Machine Gun, Shotgun, Rocket; hết đạn thì về Cannon).

## Gợi ý cách chơi mỗi trận
1. Trận 1: chơi tự nhiên, không đọc gì thêm.
2. Trận 2: thử **chỉ đi cướp cờ, bỏ qua kill** xem có thắng được không.
3. Trận 3: thử **chỉ săn kill, bỏ qua cờ** xem có thua không.
   (ghi lại build lõi bạn chọn và cảm giác từng lõi)
4. Trận 4: nhấn M đổi chế độ (3 đội hoặc solo) và chơi một trận.
5. Trận 5: tự do, thử vũ khí và vật phẩm.

## Ghi kết quả
Điền sau mỗi người chơi xong 4–5 trận. Điểm 1 (rất tệ) đến 5 (rất tốt).

| # | Câu hỏi | Điểm | Ghi chú |
|---|---|---|---|
| 1 | Lái tank có đã tay không? | | |
| 2 | Ngắm có phản hồi nhanh không? | | |
| 3 | Bắn có đã tay không? | | |
| 4 | Đọc được ai trúng ai, đạn đi đâu không? | | |
| 5 | Vụ nổ có đã mắt không? | | |
| 6 | Nhìn có biết ngay cờ nào của ai không? | | |
| 7 | Tranh một cờ (contest) có vui không? | | |
| 8 | Kill có giúp mà không thành điều duy nhất quan trọng? | | |
| 9 | % lãnh thổ có tạo căng thẳng không? | | |
| 10 | Lúc nào cũng có mục tiêu rõ ràng để làm không? | | |
| 11 | Tối đa 5 người có đủ tạo giao tranh hay không? | | |
| 12 | Cảm giác là một trận tank hay chỉ là bản demo kỹ thuật? | | |
| 13 | Chọn lõi mỗi phase có thú vị không, có lõi nào quá mạnh/vô dụng? | | |
| 14 | Phase cuối (x2, nhanh hơn) có tạo cao trào không? | | |
| 15 | Điểm số có phản ánh đúng ai chơi tốt không (cờ so với kill)? | | |
| 16 | Vật phẩm ngẫu nhiên có thú vị hay gây cảm giác "không công bằng"? | | |

Thêm vài câu mở:
- Lúc nào bạn thấy **chán** hoặc **không biết làm gì**?
- Lúc nào bạn thấy **bực** (bị bắn từ ngoài màn hình, bị kẹt, bot quá khó/dễ)?
- Bạn có quay lại bảo vệ cờ đã chiếm không? Vì sao có/không?
- Cờ yếu dần khi bỏ trống: bạn thấy hợp lý hay khó chịu?
- Vật phẩm/vũ khí nào hữu ích nhất, vô dụng nhất?
- Map có quá rộng hay quá thưa không?

## Giới hạn đã biết
- Bot khá "sắc" khi bắn và không phối hợp; chưa có spawn protection.
- Chưa có âm thanh; HUD là bản debug.
- Chưa hỗ trợ mobile và chưa có multiplayer.
- Nếu game đứng hình hoặc lỗi, ghi lại thời điểm và việc đang làm, rồi gửi kèm file log: `~/Library/Logs/Unity/Player.log`.
