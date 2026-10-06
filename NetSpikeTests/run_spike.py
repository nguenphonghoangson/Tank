#!/usr/bin/env python3
"""
Runs the networking spike unattended: one headless server and two scripted bot clients on this Mac, under several
simulated network conditions, and collects each process's metrics JSON.

  python3 NetSpikeTests/run_spike.py [seconds-per-condition]

Needs the player built from Assets/Scenes/NetSpike.unity at Builds/macOS_NetSpike/NetSpike.app.
Results go to Benchmarks/net_spike/<condition>/{server,client1,client2}.json plus the process logs.
"""
import json, os, subprocess, sys, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# --match: the build with the real Crossfire map, items and dash (Builds/macOS_NetSpike_Match), results in Benchmarks/net_spike_match
MATCH = "--match" in sys.argv
if MATCH: sys.argv.remove("--match")
BIN = os.path.join(ROOT, "Builds/macOS_NetSpike_Match/NetSpike.app/Contents/MacOS/Tank" if MATCH else "Builds/macOS_NetSpike/NetSpike.app/Contents/MacOS/Tank")
OUT = os.path.join(ROOT, "Benchmarks/net_spike_match" if MATCH else "Benchmarks/net_spike")
SECONDS = int([a for a in sys.argv[1:] if a.isdigit()][0]) if any(a.isdigit() for a in sys.argv[1:]) else 40
# one-way latency is applied on every side, so RTT is about twice the value
CONDITIONS = [("lan", 0, 0), ("rtt100", 50, 0), ("rtt150_loss2", 75, 2)]
# A/B of the server jitter buffer (the same build; extra arguments per condition)
EXTRA = {"rtt100_jb2": ["-jitterBuffer", "2"], "rtt150_loss2_jb2": ["-jitterBuffer", "2"]}
AB = [("rtt100_jb2", 50, 0), ("rtt150_loss2_jb2", 75, 2)]


def launch(args, log):
    return subprocess.Popen([BIN, "-batchmode", "-nographics", "-logFile", log] + args, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def run(name, lat, loss, port):
    d = os.path.join(OUT, name)
    os.makedirs(d, exist_ok=True)
    for f in os.listdir(d):
        os.remove(os.path.join(d, f))
    net = ["-latencyMs", str(lat), "-lossPct", str(loss), "-port", str(port)] + EXTRA.get(name, [])
    procs = [launch(["-spikeServer", "-spikeSeconds", str(SECONDS + 6), "-spikeOut", os.path.join(d, "server.json")] + net, os.path.join(d, "server.log"))]
    time.sleep(4)
    for i in (1, 2):
        procs.append(launch(["-spikeClient", "127.0.0.1", "-spikeBot", "-spikeSeconds", str(SECONDS), "-spikeOut", os.path.join(d, "client%d.json" % i)] + net, os.path.join(d, "client%d.log" % i)))
        time.sleep(0.5)
    deadline = time.time() + SECONDS + 40
    while time.time() < deadline and any(p.poll() is None for p in procs):
        time.sleep(1)
    for p in procs:
        if p.poll() is None:
            p.kill()
    time.sleep(1)


def load(path):
    return json.load(open(path)) if os.path.exists(path) else None


def main():
    global CONDITIONS
    if "--ab" in sys.argv:
        sys.argv.remove("--ab")
        CONDITIONS = AB
    if not os.path.exists(BIN):
        sys.exit("player not found: " + BIN)
    for i, (name, lat, loss) in enumerate(CONDITIONS):
        print("running", name, "one-way %d ms, loss %d%%" % (lat, loss), flush=True)
        run(name, lat, loss, 7777 + i)
    print()
    for name, lat, loss in CONDITIONS:
        d = os.path.join(OUT, name)
        s, c1, c2 = load(os.path.join(d, "server.json")), load(os.path.join(d, "client1.json")), load(os.path.join(d, "client2.json"))
        print("==", name)
        if not (s and c1 and c2):
            print("   missing results:", [k for k, v in (("server", s), ("client1", c1), ("client2", c2)) if not v])
            continue
        for tag, c in (("client1", c1), ("client2", c2)):
            print("   %s: rtt p50 %.0f ms | corrections %.1f/min (>0.5 m: %d, max %.2f m) | underruns %d | shots %d hits %d kills %d | agreed %d/%d seen-on-screen hits | confirm p50/p95 %s/%s ms | in %s B/s out %s B/s" % (
                tag, c["rtt_ms"].get("p50", 0), c["corrections_per_min"], c["corrections_over_0_5m"], c["correction_max_m"], c["interp_underruns"],
                c["shots"], c["hits_confirmed"], c["kills"], c["expected_hits_confirmed_by_server"], c["expected_hits_seen_on_screen"], c["fire_to_confirm_ms_minus_travel"].get("p50", "-"), c["fire_to_confirm_ms_minus_travel"].get("p95", "-"),
                c["bytes_in_per_s"], c["bytes_out_per_s"]))
        print("   server: ticks %d, shots accepted %d (clients predicted %d), starved %d, dropped %d, out %s B/s, in %s B/s" % (s["server_ticks"], s["server_fires"], c1["shots"] + c2["shots"], s["server_starved_ticks"], s["server_dropped_cmds"], s["bytes_out_per_s"], s["bytes_in_per_s"]))
        if "pickups_taken_repair_shield_speed_damage" in s:
            print("   items taken (repair/shield/speed/damage) %s, dashes started %s, weapons (mg/shotgun/rocket) %s, flags captured %s, score blue/red %s, client corrections/min %s / %s" % (s["pickups_taken_repair_shield_speed_damage"], s["dashes_started"], s.get("weapon_pickups_taken_mg_shotgun_rocket"), s.get("flags_captured"), s.get("score_blue_red"), c1["corrections_per_min"], c2["corrections_per_min"]))


if __name__ == "__main__":
    main()
