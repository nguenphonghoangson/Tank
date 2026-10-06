# Networking: yêu cầu và các hướng (bước 1, chưa cài gì)

Trạng thái: **tài liệu quyết định, chưa chọn giải pháp, chưa cài thư viện mạng nào.** Dựa trên GDD v0.4 và code prototype hiện tại. Thông tin về sản phẩm bên ngoài (giá, phiên bản, hỗ trợ Unity) lấy từ tài liệu công bố và bài viết tổng hợp tìm được, **cần xác minh bằng spike** trước khi dựa vào. Tài liệu nội bộ.

## 1. Game cần gì từ mạng
| Yêu cầu | Từ đâu | Hệ quả |
|---|---|---|
| 2 đến 5 người mỗi trận, trận ngắn (4 phase, 240 s) | GDD §3, §4 | Băng thông nhỏ; không cần scale lớn. Quan trọng là **độ trễ**, không phải số người |
| Bắn nhanh, đạn 55 m/s, tank 14 m/s, hạ gục sau 4 phát Cannon | GDD §7 | Phản hồi tức thì cho người bắn (**client prediction**), xác nhận trúng phải công bằng (**lag compensation**) |
| PC/Mac và iPhone (sau này Android) | mục tiêu dự án | Mạng di động (4G/Wi-Fi), mất gói, đổi mạng giữa trận |
| Có bot lấp slot, có điểm, cờ, item ngẫu nhiên, chọn lõi theo phase | GDD §3, §6 to §9 | Logic trận phải có **một chủ duy nhất** (server hoặc host); bot chạy ở chủ đó |
| Sau này có xếp hạng | GDD §6 | Nên chống gian lận ở mức cơ bản: **server cầm quyền** thì tốt hơn host là người chơi |

Dữ liệu cần đồng bộ (ước tính, nhỏ): mỗi tank gồm vị trí, vận tốc, góc thân và góc nòng, HP, đạn, buff, danh sách lõi; đạn (sinh ra từ lệnh bắn, mô phỏng cục bộ); cờ (chủ, hold, tiến độ, contested, chỉ server tính); ô item (loại đang hiện); phase, hạn giờ chọn lõi, điểm. Lệnh người chơi (`TankCommand`) khoảng 10 byte mỗi tick.

## 2. Code hiện tại sẵn sàng đến đâu
**Điểm tốt:** mọi nguồn input chỉ ghi `TankCommand`, simulation (`TankUnit`) không biết input đến từ đâu, hiệu ứng (`TankFeedback`, `CombatFx`) chỉ nghe sự kiện. Đây đúng là chỗ cắm mạng.

**Cần đổi trước khi đồng bộ được** (khoảng 1.600 dòng code mô phỏng, 44 chỗ dùng `Time`/`Random`):
| Vấn đề | Chi tiết | Hướng xử lý |
|---|---|---|
| Hit-stop và pause dùng `Time.timeScale` toàn cục | Làm chậm cả mô phỏng và vật lý. Không thể có "pause khi bạn chọn lõi" trong game nhiều người | Hit-stop chỉ làm **hiệu ứng hình ảnh cục bộ** (không đổi mô phỏng); chọn lõi thành **cửa sổ có hạn giờ**, bot và người chơi khác không bị đứng |
| Di chuyển dùng Rigidbody, vận tốc đặt trực tiếp | Vật lý Unity không xác định giữa các máy; dự đoán và sửa sai khó | Mô phỏng di chuyển theo tick cố định, tách khỏi Rigidbody hoặc dùng kinematic và tự giải va chạm; đây là phần tốn công nhất |
| Đạn dùng `deltaTime` và SphereCast mỗi frame | Server cần bước theo tick; client chỉ vẽ | Server cầm quyền sát thương; client hiển thị đạn sinh từ lệnh bắn |
| `MatchManager` gom tất cả (phase, điểm, cờ, respawn, offer lõi), HUD đọc trực tiếp | Một nơi giữ trạng thái, client chưa có bản sao | Tách trạng thái trận có thể sao chép; HUD đọc bản sao trên client |
| Ngẫu nhiên (offer lõi, item) dựa trên `System.Random`/`UnityEngine.Random` cục bộ | Mỗi máy ra kết quả khác nhau | Chỉ chủ trận roll; kết quả được gửi đi |
| Bot chạy trên từng tank cục bộ | Phải chạy ở chủ trận | Giữ `BotBrain`, chỉ bật trên server/host |
| Sự kiện C# (`Fired`, `Damaged`, `Died`...) chỉ cục bộ | Client khác không nhận | Chuyển thành sự kiện được sao chép (RPC/trạng thái) |

## 3. Các hướng
**Hosting là điều cần chú ý riêng:** Unity ngừng hỗ trợ trực tiếp *Multiplay Game Server Hosting* từ 31/3/2026 (phần mềm được cấp phép cho Rocket Science Group tiếp tục vận hành); Hathora đóng nền tảng từ 5/5/2026. Unity **Relay** vẫn hoạt động (gói Relay/Lobby riêng lẻ đã deprecated, dùng `com.unity.services.multiplayer`). Lựa chọn hosting còn lại: Edgegap, Gameye, GameFabric (Nitrado), Rocket Science Group, hoặc tự chạy (VPS).

| | Prediction và lag comp | Mô hình quyền | Chi phí (theo nguồn công bố) | Phù hợp với code hiện tại | Ghi chú và rủi ro |
|---|---|---|---|---|---|
| **Photon Fusion 2** | Có sẵn: mô phỏng theo tick, prediction, lag compensation (Host Mode và Server Mode) | Shared, Host, hoặc Server (server cần tự host headless) | Miễn phí 100 CCU cho game; 500 CCU khoảng 125 USD/tháng; Server Mode trả thêm tiền hạ tầng | Phải viết lại di chuyển, đạn, trận theo mô hình tick của Fusion | Phụ thuộc Photon. Danh sách hỗ trợ Unity công bố 6.0 và 6.3, **chưa thấy ghi 6.6**: phải thử. Có sẵn sample **Tanknarok** (tank top-down, shared authority và client-host). Máy này có tải sẵn Fusion Tanknarok 2.1.1 trong Downloads, tức là đã có người xem |
| **FishNet (Pro)** | Client-side prediction trưởng thành, ít rác bộ nhớ | Server cầm quyền hoặc host | Pro 46 euro một lần (Asset Store); tự host, không phí CCU | Cần viết lại di chuyển theo mô hình replicate/reconcile | Hỗ trợ Unity 6. Cần tự lo relay/matchmaking/hosting. Cộng đồng nhỏ hơn Mirror/NGO |
| **Netcode for GameObjects (NGO) 2.x** | Prediction có nhưng ít trưởng thành, nhiều việc thủ công | Server cầm quyền hoặc Distributed Authority | Miễn phí; Relay 50 CCU miễn phí rồi 0.16 USD/CCU | Gần với stack mặc định của Unity | Package chính chủ, tài liệu đến bản 2.9. Chưa thấy lag compensation dựng sẵn |
| **Mirror** | Không có prediction dựng sẵn | Server cầm quyền hoặc host | Miễn phí | Tự làm hết phần khó | Hợp game chậm hơn; ít hợp đạn nhanh cần prediction |
| **Nakama (authoritative match)** | Không (logic server viết bằng Go/TS/Lua, tick 1 đến 60 Hz) | Server cầm quyền | Tự host; studio **đã có `nakama-template` và hướng dẫn** | Phải **viết lại toàn bộ mô phỏng** bằng ngôn ngữ server, trùng logic với client | Hợp làm **tài khoản, matchmaking, bảng xếp hạng** (sau này), ít hợp làm lõi mô phỏng bắn nhanh |
| Photon Quantum (xác định) | Có (rollback) | Giả lập xác định | Tính CCU | Viết lại gần như toàn bộ theo ECS | Quá nặng cho prototype; loại |

Những gì **không** nên làm: tự viết netcode từ đầu; để Nakama chạy mô phỏng bắn; chọn theo cảm tính.

## 4. Đề xuất
Spike hai ứng viên có prediction trưởng thành trên đúng bài toán này: **Photon Fusion 2** (Host Mode trước, Server Mode sau) và **FishNet Pro**. NGO là phương án dự phòng nếu muốn ở trong hệ sinh thái Unity và chấp nhận làm prediction thủ công. Nakama giữ cho tầng tài khoản/matchmaking về sau. Quyết định cuối cùng sau spike, theo số đo (mục 5), không theo tài liệu này.

Việc đầu tiên, rẻ nhất: **kiểm tra tương thích Unity 6000.6.4f1** của Fusion 2 và FishNet trên một nhánh thử, trước khi mất công làm gì khác. Nếu Fusion không chạy trên 6.6, ứng viên thứ nhất phải đổi.

## 5. Kế hoạch spike (làm trên nhánh riêng `spike/networking-*`, không đụng `main`)
1. **Tương thích (1 giờ đến nửa ngày):** nhập thư viện vào nhánh thử, mở project, build macOS và iOS. Ghi lại lỗi.
2. **Bản chạy nhỏ nhất (mỗi ứng viên 3 đến 5 ngày):** hai thiết bị (Mac và iPhone) cùng vào một trận có một cờ, di chuyển, bắn, trúng, chết, hồi sinh. Chưa cần chọn lõi hay item.
3. **Đo** bằng Network Link Conditioner (macOS/iOS) với các cấu hình: LAN, Wi-Fi 40 ms, 4G 100 ms, 4G 150 ms + 2% mất gói.

| Chỉ số | Cách đo | Ngưỡng tham khảo (cần studio chốt) |
|---|---|---|
| Phản hồi lái và bắn của chính mình | Từ chạm/phím đến thay đổi trên màn hình | Không tăng so với bản offline (dự đoán tại chỗ) |
| Giật vị trí (rubber-banding) | Số lần tank bị kéo lại hơn 0.5 m mỗi phút | Rất hiếm ở 100 ms |
| Độ mượt của tank người khác | Nhìn và quay video; đo độ trễ nội suy | Không có bước nhảy nhìn thấy |
| Từ bắn đến xác nhận trúng | Thời gian từ lệnh bắn đến sát thương hiện trên cả hai máy | Gần RTT cộng một tick; không bị "bắn trúng mà không ăn" ở 100 ms |
| Băng thông mỗi client | Profiler/Charles | Vài KB/s |
| CPU và nhiệt trên iPhone | Xcode Instruments / FPS trong game | Không tụt dưới 50 FPS so với offline |
| Công sức chuyển code | Số dòng sửa/viết lại | Ghi lại để so sánh |

## 6. Quyết định cần từ studio
1. **Cross-play** PC/Mac và mobile cùng một trận, hay tách? (ảnh hưởng độ trễ mục tiêu và matchmaking)
2. **Quyền:** server cầm quyền (tốn hosting nhưng chống gian lận, cần cho xếp hạng) hay host là người chơi (rẻ, dễ làm trước)?
3. **Khu vực phát hành** và vị trí server (Việt Nam, Đông Nam Á?). Ngưỡng độ trễ chấp nhận được.
4. **Ngân sách** hạ tầng hàng tháng và mục tiêu CCU ban đầu.
5. **Matchmaking và tài khoản:** dùng Nakama studio đã có, hay dịch vụ của nhà cung cấp mạng?
6. **Thiết kế đổi:** chọn lõi thành cửa sổ có hạn giờ không dừng trận; hit-stop chỉ là hiệu ứng cục bộ.
7. **Mức chống gian lận** tối thiểu (liên quan quyết định 2).

## 7. Rủi ro và điều chưa chắc
- Thông tin giá, phiên bản, hỗ trợ Unity có thể thay đổi; một phần lấy từ bài viết tổng hợp của bên thứ ba. Cần đọc lại trang chính thức khi chốt.
- Hỗ trợ **Unity 6000.6** của từng thư viện chưa xác minh.
- Việc tách di chuyển khỏi Rigidbody có thể tốn hơn dự kiến và cũng ảnh hưởng cảm giác lái mà team vừa chốt là ổn. Cần giữ nguyên các thông số cảm giác (tốc độ 14 m/s, phanh gắt, không trượt) khi chuyển.
- Hai package AI vừa thêm vào `manifest.json` (không phải của networking) nên được kiểm tra xem có xung đột khi nhập thư viện mạng.
- Mô hình Host Mode (người chơi làm host) rẻ nhưng host dùng mạng di động sẽ làm cả phòng giật.

## Nguồn
- [Netcode for GameObjects: Authority (2.8)](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.8/manual/terms-concepts/authority.html), [Distributed authority quickstart (2.9)](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.9/manual/learn/distributed-authority-quick-start.html)
- [Photon Fusion 2 intro](https://doc.photonengine.com/fusion/v2/fusion-intro), [Fusion 2 SDK download](https://doc.photonengine.com/fusion/v2/getting-started/sdk-download), [Photon Fusion pricing 2026 (bên thứ ba)](https://crux.supercraft.host/blog/photon-fusion-pricing-2026/), [Free 100 CCU](https://blog.photonengine.com/new-free-100-ccu-for-photon-fusion-and-quantum-games/)
- [FishNet Pro (Asset Store)](https://assetstore.unity.com/packages/tools/network/fishnet-pro-networking-evolved-287711), [FishNet docs](https://fish-networking.gitbook.io/docs/overview/readme/pro-projects-and-support)
- [UGS pricing](https://unity.com/products/gaming-services/pricing), [Multiplay hosting pricing](https://docs.unity.com/ugs/manual/game-server-hosting/manual/concepts/pricing), [Multiplay transition (AccelByte)](https://accelbyte.io/blog/unity-multiplay-is-transitioning-practical-paths-forward-for-multiplayer-studios-using-or-evaluating-multiplay), [Game server shake-up 2026 (Gameye)](https://gameye.com/blog/game-server-shake-up-2026/)
- [Nakama authoritative multiplayer](https://heroiclabs.com/docs/nakama/concepts/multiplayer/authoritative/)
- [Choosing a real-time networking stack for Unity in 2026 (DEV)](https://dev.to/gamedevtoollab/choosing-the-right-real-time-networking-stack-for-unity-in-2026-29f4), [FishNet, Mirror, Photon compared](https://oceanviewgames.co.uk/blog/posts/cross-platform-multiplayer-networking-unity)
