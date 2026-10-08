#!/usr/bin/env python3
"""
Runs the multiplayer build unattended: one headless dedicated server and N autoplay clients on this Mac, then prints what each logged.

  python3 GameTests/run_net.py [seconds] [clients] [server_bots] [extra args for every process, e.g. -latencyMs 50 -lossPct 2]

Needs the player built from Assets/Scenes/Game_Arena.unity at Builds/macOS_Game/Tank.app. Logs go to GameTests/out/.
"""
import os, re, subprocess, sys, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BIN = os.path.join(ROOT, "Builds/macOS_Game/Tank.app/Contents/MacOS/Tank")
OUT = os.path.join(ROOT, "GameTests/out")
args = [a for a in sys.argv[1:]]
SECONDS = int(args[0]) if len(args) > 0 else 30
CLIENTS = int(args[1]) if len(args) > 1 else 1
BOTS = int(args[2]) if len(args) > 2 else 1
EXTRA = args[3:]


def launch(name, flags):
    log = os.path.join(OUT, name + ".log")
    if os.path.exists(log): os.remove(log)
    return subprocess.Popen([BIN, "-batchmode", "-nographics", "-logFile", log] + flags + EXTRA, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL), log


def main():
    os.makedirs(OUT, exist_ok=True)
    procs = []
    server, slog = launch("server", ["-server", "-bots", str(BOTS), "-logstats", "-quitAfter", str(SECONDS + 8)])
    procs.append(server)
    time.sleep(4)
    logs = {"server": slog}
    for i in range(CLIENTS):
        p, l = launch("client%d" % (i + 1), ["-client", "-address", "127.0.0.1", "-autoplay", "-name", "Tester%d" % (i + 1), "-logstats", "-quitAfter", str(SECONDS)])
        procs.append(p); logs["client%d" % (i + 1)] = l
        time.sleep(0.7)
    deadline = time.time() + SECONDS + 40
    while time.time() < deadline and any(p.poll() is None for p in procs):
        time.sleep(1)
    for p in procs:
        if p.poll() is None: p.kill()
    time.sleep(1)
    for name, path in logs.items():
        print("=== " + name)
        if not os.path.exists(path): print("  (no log)"); continue
        text = open(path, errors="replace").read()
        net = [l for l in text.splitlines() if l.startswith("[net]")]
        errs = [l for l in text.splitlines() if ("Exception" in l or "Error" in l or "error" in l) and "[net]" not in l]
        for l in net[::max(1, len(net)//5)][:6] + net[-1:]: print("  " + l)
        print("  net lines: %d, error-like lines: %d" % (len(net), len(errs)))
        for l in errs[:6]: print("  ! " + l[:200])


main()
