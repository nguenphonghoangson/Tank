# Networking spike results (Mirror v96.11.3, Unity 6000.6.4f1)

Internal / confidential. Branch `spike/mirror-compat`. Code: `Assets/NetSpike`, runner: `NetSpikeTests/run_spike.py`, raw JSON: `Benchmarks/net_spike/`.

## What was built

Server-authoritative Mirror prototype, fixed 30 Hz tick: quantized input commands with sequence numbers (last 3 sent redundantly, unreliable), server input queue, per-tick snapshots with ackSeq, client prediction with replay on mismatch (>0.05 m or >2°), smoothed visual error offset, interpolation of remote tanks (~3 ticks behind), lag-compensated shooting (client-reported viewTick, 64-tick history, 20-tick clamp), ray-vs-OBB hit test, damage after shell travel time, hit confirmation via TargetRpc, respawn with epoch.

## Test setup and limits

Headless server plus 2 bot clients on one Mac, 40 s per condition, latency and loss from Mirror `LatencySimulation` (localhost, not a real network). Bots only orbit and shoot. Not covered: tank-vs-tank collision, flags, items, cores, real Wi-Fi/4G, iPhone client, IL2CPP/iOS build. Sample sizes are small (about 25 seen-on-screen hits per client), so differences of a few points are noise.

## Results (per client, 2 clients)

| Condition | RTT p50 | Corrections /min | Seen-on-screen hits agreed by server | Confirm p50 |
|---|---|---|---|---|
| LAN | ~0 ms | 1.5–6 | 22/24, 24/25 | 45–57 ms |
| One-way 50 ms | ~153 ms | 7.5–10.5 | 19/25, 23/27 | 178–196 ms |
| One-way 75 ms, 2% loss | ~204 ms | 13.5–16.5 | 19/28, 18/25 | 246–275 ms |

Bandwidth per client about 2.8 KB/s in and 2.0 KB/s out; server about 5.4 KB/s out for 2 clients. No interpolation underruns in any run. Server accepted slightly fewer shots than clients predicted at high latency (e.g. 161 vs 171).

## Server jitter buffer A/B (wait for 2 queued commands before consuming)

| Condition | Metric | Without | With `-jitterBuffer 2` |
|---|---|---|---|
| RTT ~153 ms | Corrections /min | 7.5–10.5 | 12–15 |
| RTT ~153 ms | Agreement | 19/25, 23/27 | 21/27, 22/26 |
| RTT ~153 ms | Confirm p50 | 178–196 ms | 195–244 ms |
| RTT ~204 ms, 2% loss | Corrections /min | 13.5–16.5 | 12–13.5 |
| RTT ~204 ms, 2% loss | Agreement | 19/28, 18/25 | 21/25, 23/27 |
| RTT ~204 ms, 2% loss | Server starved ticks | 26 | 13 |
| RTT ~204 ms, 2% loss | Confirm p50 | 246–275 ms | 270 ms |

The buffer reduces starvation and may help agreement under loss, but it adds latency and did not reduce corrections at moderate RTT. Not a clear win; left off by default.

## Reading

Mirror can carry this model: prediction, reconciliation, interpolation and lag-compensated hits all work end to end, bandwidth is small, and the package compiles and weaves on 6000.6.4f1. Feel degrades with latency: at RTT ~200 ms roughly 3 in 10 hits seen on screen are rejected by the server, and corrections occur every 4–5 s. Whether that is acceptable has to be judged by playing, not by these counters.

## Open items

- Cause of the remaining disagreement is not isolated (candidates: repeat-last-input divergence while the server queue is starved, viewTick vs interpolation offset, packet loss on redundancy).
- `SpikeMetrics` still contains `Dbg*` diagnostic counters to remove before any reuse.
- Unverified: real network, iPhone as client, collisions, host vs dedicated server split.
- Studio decisions (cross-play, region, hosting, budget, matchmaking) remain in `Networking_Requirements_and_Options.md` §6.

## Match scene: real map, items, dash (`NetSpike_Match`)

Crossfire (120 x 120, 20 spawn points, 14 item slots) imported as geometry; flags are inert (not networked yet). Dash is part of the predicted simulation (`SpikeSim`), the speed item is a predicted state, shield/damage/repair are server-only. Items are rolled by the server (weighted random, respawn 25–40 s) and synced as one byte per slot; collection is decided from the authoritative tank position. Bots seek the nearest item and dash every 5 s. 60 s runs, 2 bots:

| Condition | Items taken (repair/shield/speed/damage) | Dashes | Corrections /min | Seen-on-screen hits agreed |
|---|---|---|---|---|
| LAN | 7 / 4 / 7 / 1 | 18 | 6, 9 | 32/39, 26/32 |
| RTT ~155 ms | 7 / 6 / 5 / 1 | 21 | 5, 12 | 26/37, 17/20 |
| RTT ~204 ms, 2% loss | 7 / 5 / 4 / 1 | 17 | 15, 11 | 26/32, 24/32 |

Server bandwidth rose to ~7.5 KB/s out (snapshot carries dash/speed/shield fields). Weapons pickups, cores, flags and scoring are not in the spike yet.
