# GDD: Tank Objective Match (prototype v0.4)

Trạng thái: **prototype chơi được trên macOS và iPhone.** Playtest nội bộ lần 1 (team, bản macOS): đánh giá ổn, chưa có yêu cầu chỉnh. Thử trên iPhone 13 (bản có điều khiển cảm ứng và HUD mới): lái không còn nặng, chưa có số FPS ghi lại. Phản hồi mới ở mức tổng quan, chưa có điểm số theo từng câu hỏi. Các con số dưới đây lấy từ code (`Assets/Prototype/`), chưa được cân bằng bằng dữ liệu. Tài liệu nội bộ, không chia sẻ ra ngoài.

## 1. Tóm tắt
PvP tank shooter theo **mục tiêu**, tối đa **5 người** mỗi trận (solo, 2 đội hoặc 3 đội). Người chơi lái tank, bắn nhau, **giữ cờ** để kiếm điểm, và mỗi phase chọn một **lõi (core)** ngẫu nhiên để build tank. Sau 4 phase, bên có tổng điểm cao nhất thắng.

**Giả thuyết cần kiểm chứng:**
1. Khi kill không quyết định thắng, người chơi liên tục phải chọn giữa "bắn" và "đi giữ/cướp cờ", và lựa chọn đó vui.
2. Build ngẫu nhiên theo phase làm mỗi trận khác nhau và tạo cảm giác mạnh dần.
3. Phase cuối (điểm x2, nhịp nhanh hơn) tạo cao trào.
4. Cảm giác bắn nhanh, nặng, rõ (muzzle, recoil, impact, nổ) đủ để một trận giống "trận tank" chứ không phải demo kỹ thuật.

## 2. Vòng lặp
Vào map → (mỗi phase) chọn lõi → lái tới cờ → đánh nhau để cướp/giữ → nhặt item → chết, hồi sinh → phase tiếp theo (điểm và nhịp tăng) → hết 4 phase → tổng điểm cao nhất thắng.

## 3. Cấu trúc trận
- Tổng thời gian chơi 240 s, chia đều 4 phase (60 s mỗi phase). Thời gian chọn lõi không tính vào.
- Đầu mỗi phase: mỗi người nhận 3 lõi ngẫu nhiên (không trùng lõi đã có), chọn 1 trong 15 s (hết giờ thì tự chọn). **Trận tạm dừng** trong lúc người thật chọn. Bot chọn ngẫu nhiên ngay.

| Phase | Hệ số điểm | Nhịp (cướp/decay cờ) | Hồi sinh | Item hồi |
|---|---|---|---|---|
| 1 | x1 | x1 | 3 s | x1 |
| 2 | x1 | x1 | 3 s | x1 |
| 3 | x1.25 | x1.15 | 2.5 s | x0.8 |
| 4 (FINAL) | **x2** | **x1.5** | **1.5 s** | **x0.5** |

## 4. Người chơi và đội
Mô hình chỉ biết **chỉ số đội**: solo = mỗi người một đội, nên cùng một hệ objective chạy cho 1 đến 5 người, 1 đến 5 đội. Preset để thử: 2v2 (mặc định), 3v2, ba đội 2-2-1, solo 5. Các slot không có người được bot lấp.

## 5. Map và cờ
- Map là **prefab** chứa một `MapLayout` (spawn, cờ, ô item, vật cản), thêm map mới không phải sửa code. Hiện có một map: **Crossfire**, 120x120 m, 5 cờ (A tây, B giữa, C đông, D nam, E bắc), spawn ở rìa (nam, bắc, đông, tây, góc tây-nam).
- Cờ bán kính 8 m, trọng số 1 (B: 1.5). Trạng thái: neutral, owned, contested.
- **Cướp:** một đội đứng một mình thì cướp; 1 tank mất 6 s, 2 tank 1.5x nhanh, 3 tank 1.75x (giảm dần để xếp chồng không luôn tối ưu). Có địch cùng đứng trong vòng thì cờ **đóng băng**.
- **Chiếm lại:** cờ đang có chủ phải bị hạ về neutral trước rồi mới chiếm được (khoảng 12 s cho một tank), nên chiếm được thì cũng mất được.
- **Cần bảo vệ:** cờ không có người của chủ giữ sẽ **yếu dần (về neutral sau 45 s)**; người của chủ đứng trong vòng thì hồi nhanh hơn lúc cướp 1.5x. Hiển thị bằng lá cờ kéo lên/hạ xuống và vòng tô màu.

## 6. Điểm số và thắng
| Nguồn | Điểm (x hệ số phase) |
|---|---|
| Cờ đang giữ | 3 điểm/s mỗi cờ (x trọng số), cho cả đội |
| Capture | 100 (mỗi người trong vòng) |
| Kill | 50 (x Bounty Hunter) |
| Assist | 25 (sát thương trong 5 s trước khi chết) |
| Defense | 40 (kill địch đang ở trong hoặc sát cờ của đội mình, bán kính +4 m) |
| Contest | 20 (vào vòng cờ đối phương và làm nó contested) |

- **Thắng = tổng điểm đội cao nhất** sau 4 phase. Hòa điểm thì xét % cờ nắm giữ trung bình, vẫn hòa thì hòa.
- Giữ 1 cờ trong 60 s ≈ 3 đến 4 kill. Đây là cơ chế để "kill không thay thế cờ". Chỉnh `flagIncomePerSecond` và `ScoreConfig` để đổi tỉ lệ.
- Điểm cá nhân (K/D/A/Cap/Def/Con) dùng cho bảng điểm và MVP, độc lập với kết quả đội. Dữ liệu để làm ranking sau này đã được ghi nhận, chưa có ranking/MMR.
- Có sẵn luật **domination** (một đội giữ cả 5 cờ liên tục N giây thì thắng ngay), đang tắt.

## 7. Tank
- Điều khiển: WASD di chuyển (theo hướng màn hình), chuột ngắm (nòng quay độc lập, 720°/s), chuột trái bắn, R reload, Space/Shift dash.
- Lái kiểu xích: vận tốc luôn theo hướng thân xe, không trượt ngang, phanh gắt. Tốc độ 14 m/s, thân xe quay 540°/s. Knockback và recoil tách riêng, tắt nhanh, tỉ lệ với sát thương.
- 100 HP, hồi sinh sau 1.5 đến 3 s, mất vũ khí đặc biệt và buff khi chết.
- Mọi nguồn input (bàn phím/chuột, bot, sau này là mobile/mạng) chỉ ghi `TankCommand` (Move, AimPoint, Fire, Reload, Skill); simulation chỉ đọc struct này.

**Vũ khí** (`WeaponDef`, thêm vũ khí = thêm asset):
| Vũ khí | Sát thương | Nhịp | Tốc độ đạn | Đạn | Ghi chú |
|---|---|---|---|---|---|
| Cannon (mặc định) | 25 | 0.33 s | 55 m/s | 10, reload 1.6 s | splash 2.2 m (40%), tầm 70 m |
| Machine Gun (nhặt) | 8 | 0.085 s | 70 m/s | 45 phát | tản 3.5°, tầm 48 m |
| Shotgun (nhặt) | 9 x 7 viên | 0.75 s | 44 m/s | 7 phát | tản 18°, tầm 26 m |
| Rocket (nhặt) | 55 | 1.0 s | 32 m/s | 4 quả | splash 5.5 m (60%) |

Vũ khí nhặt có số phát giới hạn, hết thì về Cannon.

**Skill:** Dash (36 m/s trong 0.22 s, cooldown 6 s). Thêm skill = thêm một lớp con nhỏ của `TankSkill`.

**Vật phẩm** (mỗi ô random loại theo trọng số mỗi lần xuất hiện; một số ô ban đầu để trống; hồi 25 đến 45 s, đổi theo phase):
| Item | Hiệu ứng | Trọng số |
|---|---|---|
| Repair | +40 HP (chỉ nhặt khi thiếu máu) | 3 |
| Shield | hấp thụ 50 sát thương, 12 s | 2 |
| Speed | x1.4 tốc độ, 8 s | 2 |
| Damage | x1.5 sát thương, 10 s | 1.5 |
| Machine Gun / Shotgun | vũ khí đặc biệt | 1.5 / 1.5 |
| Rocket | vũ khí đặc biệt | 1 |

## 8. Lõi (core)
Cộng dồn qua 4 phase, nhiều lõi có đánh đổi. Pool 12 lõi trong `CoreLibrary`:
Heavy Plating (+40 HP, -8% tốc độ), Overdrive Engine (+18% tốc độ), Heavy Shells (+30% sát thương, bắn chậm 15%), Rapid Loader (bắn nhanh 22%, -10% sát thương), Quick Hands (reload -40%, +40% băng đạn), Blast Rounds (+1.8 m splash), Afterburner (dash cooldown -45%), Repair Nanites (hồi 4 HP/s khi không bị bắn), Zone Engineer (cướp/gia cố cờ nhanh hơn 50%), Bounty Hunter (điểm kill/assist +50%), Vampiric Rounds (hồi 25% sát thương gây ra), Glass Cannon (+50% sát thương, -25 HP).

Lõi hiện chỉ đổi chỉ số. "Lõi đặc biệt" đổi hẳn cách chơi (đạn nảy, tàng hình...) chưa làm, chỉ xét sau khi biết hệ lõi cơ bản có hấp dẫn không.

## 9. Bot
Bot lấp slot trống và dùng để test. Hành vi: chọn cờ không an toàn (ưu tiên cờ yếu hoặc có địch gần), đánh địch nhìn thấy, đi nhặt Repair khi dưới 45% HP và nhặt item gần khi rảnh, tránh vật cản bằng whisker (không pathfinding). Chưa dash, chưa phối hợp, bắn khá "sắc".

## 10. Phản hồi bắn (feel)
Muzzle flash + đèn flash, recoil nòng kiểu spring, đạn có trail theo màu từng vũ khí, impact VFX, enemy flash trắng/xanh (shield), health bar, nổ lớn khi phá (fireball, sparks, khói, mảnh vỡ), camera kick/shake (tỉ lệ với nhịp bắn và sát thương), **hit-stop chỉ khi phá tank** (tối đa một lần mỗi 0.4 s). Có hook âm thanh (bắn, trúng, nổ) nhưng **chưa có clip**.

## 11. Camera, điều khiển và UI
- Camera chéo nhìn từ trên (FOV 55), bám tank, hơi nghiêng theo hướng ngắm (nhẹ, tối đa 4 m).
- **PC/Mac:** WASD, chuột, R, Space/Shift, Tab, F5, M. **Điện thoại (cảm ứng):** stick trái lái, stick phải ngắm (kéo quá 30% thì bắn), nút Dash và Reload ở mép phải. Hai nguồn đều chỉ ghi `TankCommand`. Stick đạt tốc độ tối đa ở 45% bán kính, vùng chết nhỏ, bán kính 0.38 inch.
- **HUD (IMGUI tạm), một bố cục duy nhất** (`HudLayout`, chiều cao tham chiếu cố định, tôn trọng safe area): trên-trái thẻ người chơi (HP, vũ khí/đạn, K/D/A, điểm, buff, lõi); trên-giữa thanh phase + đồng hồ + điểm từng đội + thanh lãnh thổ, dưới là log tối đa 3 dòng; trên-phải mini map; hai góc dưới chỉ dành cho ngón tay; Dash/Reload xếp dọc ở mép phải. Chọn lõi và kết quả là overlay toàn màn hình; màn kết thúc có nút Chơi lại / Đổi chế độ. Marker cờ ở mép màn hình nhường chỗ cho các phần tử khác. Bố cục được kiểm tra tự động: 0 phần tử chồng nhau ở 5 hình dạng màn hình.
- **Chế độ hiệu năng cho điện thoại** (chỉ chạy trên máy thật): render scale 0.75, tắt HDR, shadow 35 m, bỏ đèn flash realtime của hiệu ứng, giới hạn 60 FPS. Điện thoại hiện một dòng FPS ở đáy màn hình để tinh chỉnh.

## 12. Kiến trúc (giữ độc lập với networking)
`Input / Bot / (sau này: Network) -> TankCommand -> TankUnit (di chuyển, nòng, vũ khí, máu, skill)`; `ControlPoint -> MatchManager (phase, điểm, thắng)`; hiệu ứng nằm ở `TankFeedback`/`CombatFx` chỉ nghe sự kiện của simulation. Chưa chọn thư viện mạng. Dữ liệu chỉnh được: `WeaponDef`, `MapDef`/prefab map, `ScoreConfig`, `PhaseDef`, `CoreLibrary`, tham số trên `MatchManager`.

## 13. Hiệu năng
Scene benchmark rỗng đã có baseline Development Player (macOS, Apple M4). Đã chạy được `Match_Prototype` trên iPhone 13 (cảm giác ổn); **chưa có số đo FPS/nhiệt/pin ghi lại** và chưa đo allocation của bản match. Object pooling cho đạn và VFX đã có; HUD IMGUI tạm tốn GC.

## 14. Chưa làm (cố ý)
Networking, matchmaking, tài khoản, loadout ngoài trận, progression, economy, ranking/MMR, backend, Android, UI thật, âm thanh, art thật, AI phức tạp, spawn protection, chọn map.

## 15. Rủi ro và câu hỏi mở
- **Vòng lặp có vui không?** Cần playtest (xem `Playtest/PLAYTEST.md`).
- Cân bằng: TTK (4 phát Cannon), tốc độ cướp cờ, thu nhập cờ so với kill, độ mạnh từng lõi và vũ khí, độ thưa của map 120x120.
- Bot quá sắc có thể che vấn đề thật của game.
- Cờ yếu dần khi bỏ trống: người chơi thấy hợp lý hay khó chịu?
- Item ngẫu nhiên có gây cảm giác "không công bằng" không?
- Mobile: đã thử trên một máy (iPhone 13) và lái ổn; còn cần thử máy yếu hơn, cảm giác bắn lâu dài, kích thước map và khả năng đọc mini map ở màn hình nhỏ.
- Networking: đồng bộ 5 tank, đạn, cờ và hit-stop; cần spike đo trễ trước khi chọn giải pháp.

## 16. Tiêu chí để đi tiếp
Sau 5 người chơi x 4 đến 5 trận: phần lớn nói bắn "đã tay"; thắng được bằng cách chỉ đi cướp cờ và thua khi chỉ săn kill; mỗi phase có ít nhất một lõi được chọn có chủ đích; phase cuối được nhắc đến như cao trào. Nếu không đạt, sửa luật/cân bằng trước khi làm thêm nội dung hoặc networking.
