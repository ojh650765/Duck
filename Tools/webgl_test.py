"""
Serve the WebGL build and drive a headless Chrome over it to prove it actually runs.

A Unity WebGL build can compile perfectly and still fail in a browser — a missing decompression
fallback, a shader that will not compile on GLES3, a WASM memory limit. The only test that means
anything is loading the real build in a real browser, so this does that and writes screenshots
plus the browser console log for inspection.

Usage:
    python Tools/webgl_test.py                  # serve, load, screenshot after ~25 s
    python Tools/webgl_test.py --seconds 45     # give the loader longer
    python Tools/webgl_test.py --serve-only     # just host it for manual play
"""

import argparse
import http.server
import functools
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import threading
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILD_DIR = os.path.join(ROOT, "Build", "WebGL")
OUT_DIR = os.path.join(ROOT, "Captures", "WebGL")

CHROME_CANDIDATES = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
]


class Handler(http.server.SimpleHTTPRequestHandler):
    """Static server that sets the headers Unity's loader expects."""

    def end_headers(self):
        # Unity ships .gz / .br sidecars; without the encoding header the loader has to fall
        # back to decompressing in JS, which works but is slower and hides real errors.
        path = self.path.split("?")[0]
        # Unity's compressed build names its payloads ".unityweb", not ".gz"; without the
        # matching encoding header the loader decompresses in JavaScript and logs a warning.
        if path.endswith(".gz") or path.endswith(".unityweb"):
            self.send_header("Content-Encoding", "gzip")
        elif path.endswith(".br"):
            self.send_header("Content-Encoding", "br")
        if path.endswith((".wasm", ".wasm.gz", ".wasm.br")):
            self.send_header("Content-Type", "application/wasm")
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, fmt, *args):
        pass


def free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


def find_chrome():
    for c in CHROME_CANDIDATES:
        if os.path.exists(c):
            return c
    return None


def serve(port):
    handler = functools.partial(Handler, directory=BUILD_DIR)
    httpd = http.server.ThreadingHTTPServer(("127.0.0.1", port), handler)
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    return httpd


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seconds", type=int, default=25)
    ap.add_argument("--serve-only", action="store_true")
    ap.add_argument("--port", type=int, default=0)
    args = ap.parse_args()

    if not os.path.isdir(BUILD_DIR) or not os.path.exists(os.path.join(BUILD_DIR, "index.html")):
        print("NO BUILD: expected %s/index.html" % BUILD_DIR)
        return 2

    port = args.port or free_port()
    serve(port)
    url = "http://127.0.0.1:%d/index.html" % port
    print("serving %s at %s" % (BUILD_DIR, url))

    if args.serve_only:
        print("Ctrl-C to stop.")
        while True:
            time.sleep(3600)

    chrome = find_chrome()
    if not chrome:
        print("No Chrome/Edge found; serve-only mode is still available.")
        return 3

    os.makedirs(OUT_DIR, exist_ok=True)
    profile = tempfile.mkdtemp(prefix="duckwebgl_")
    shot = os.path.join(OUT_DIR, "webgl_load.png")
    logfile = os.path.join(OUT_DIR, "chrome.log")

    cmd = [
        chrome,
        "--headless=new",
        "--disable-gpu-sandbox",
        "--use-gl=angle",
        "--use-angle=default",
        "--enable-unsafe-swiftshader",   # so it still renders on a headless GPU-less path
        "--window-size=1600,900",
        "--screenshot=" + shot,
        # Virtual time lets the Unity loader, WASM instantiation and first frames all complete
        # without us having to guess wall-clock timings.
        "--virtual-time-budget=%d" % (args.seconds * 1000),
        "--user-data-dir=" + profile,
        "--no-first-run",
        "--no-default-browser-check",
        "--enable-logging=stderr",
        "--v=1",
        url,
    ]

    print("launching headless browser for %d s of virtual time..." % args.seconds)
    with open(logfile, "w", encoding="utf-8", errors="replace") as lf:
        proc = subprocess.run(cmd, stdout=lf, stderr=subprocess.STDOUT,
                              timeout=args.seconds * 4 + 120)

    print("chrome exit=%d" % proc.returncode)
    print("screenshot: %s (%s)" %
          (shot, "written" if os.path.exists(shot) else "MISSING"))
    print("log: %s" % logfile)

    # Surface anything that looks like a real failure rather than making a human read the log.
    interesting = []
    with open(logfile, encoding="utf-8", errors="replace") as lf:
        for line in lf:
            low = line.lower()
            if any(k in low for k in ("error", "exception", "failed", "abort",
                                      "out of memory", "webgl", "unity")):
                interesting.append(line.rstrip())
    if interesting:
        print("\n--- notable browser log lines (%d) ---" % len(interesting))
        for line in interesting[:60]:
            print(" ", line[:220])

    shutil.rmtree(profile, ignore_errors=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
