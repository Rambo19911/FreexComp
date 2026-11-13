import subprocess, json, sys, os, threading
dap, exe, src, pydir = sys.argv[1:5]
env = dict(os.environ)
env['PATH'] = pydir + os.pathsep + os.pathsep.join(p for p in env['PATH'].split(os.pathsep) if 'python' not in p.lower())
p = subprocess.Popen([dap], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
seq = 0; pending = []
def send(cmd, args=None):
    global seq; seq += 1
    b = json.dumps({"seq": seq, "type": "request", "command": cmd, "arguments": args or {}}).encode()
    p.stdin.write(b"Content-Length: %d\r\n\r\n" % len(b) + b); p.stdin.flush(); return seq
def recv():
    h = b""
    while not h.endswith(b"\r\n\r\n"):
        c = p.stdout.read(1)
        if not c: raise EOFError
        h += c
    return json.loads(p.stdout.read(int(h.split(b":")[1].split(b"\r\n")[0])))
def wait(pred):
    for i, m in enumerate(pending):
        if pred(m): return pending.pop(i)
    while True:
        m = recv()
        if pred(m): return m
        pending.append(m)
def req(cmd, args=None):
    s = send(cmd, args)
    m = wait(lambda m: m["type"] == "response" and m["request_seq"] == s)
    if not m.get("success"): print("  !! %s failed: %s" % (cmd, m.get("message")))
    return m.get("body", {})
def ev(name): return wait(lambda m: m["type"] == "event" and m["event"] == name)
t=threading.Timer(40, p.kill); t.daemon=True; t.start()
R = {}
try:
    req("initialize", {"adapterID": "freexcomp", "linesStartAt1": True, "columnsStartAt1": True, "pathFormat": "path"})
    s = send("launch", {"program": exe, "stopOnEntry": False})
    ev("initialized")
    b = req("setBreakpoints", {"source": {"path": src}, "breakpoints": [{"line": 5}]})
    R["A2 breakpoint verified"] = [(x.get("verified"), x.get("line")) for x in b.get("breakpoints", [])]
    fb = req("setFunctionBreakpoints", {"breakpoints": []})
    req("configurationDone")
    st = ev("stopped"); R["A3 stopped"] = (st["body"].get("reason"), st["body"].get("description"))
    tid = st["body"]["threadId"]
    fr = req("stackTrace", {"threadId": tid})["stackFrames"]
    R["A4 stack"] = [(f["name"], f.get("source", {}).get("name"), f.get("line")) for f in fr[:3]]
    sc = req("scopes", {"frameId": fr[0]["id"]})["scopes"]
    R["A5 scopes"] = [x["name"] for x in sc]
    for x in sc:
        vs = req("variables", {"variablesReference": x["variablesReference"]}).get("variables", [])
        R["A6 vars " + x["name"]] = [(v["name"], v["value"], v.get("type")) for v in vs][:8]
    e = req("evaluate", {"expression": "a", "frameId": fr[0]["id"], "context": "watch"}); R["B evaluate a"] = (e.get("result"), e.get("memoryReference"))
    e = req("evaluate", {"expression": "&a", "frameId": fr[0]["id"], "context": "watch"}); R["B evaluate &a"] = (e.get("result"), e.get("memoryReference"))
    if e.get("memoryReference") or e.get("result"):
        mr = e.get("memoryReference") or e["result"].split()[0]
        m = req("readMemory", {"memoryReference": mr, "count": 4}); R["B readMemory &a (4 bytes, base64)"] = m.get("data")
    req("next", {"threadId": tid}); st = ev("stopped")
    fr = req("stackTrace", {"threadId": tid})["stackFrames"]; R["A7 after next"] = (st["body"].get("reason"), fr[0]["name"], fr[0].get("line"))
    req("stepOut", {"threadId": tid}); st = ev("stopped")
    fr = req("stackTrace", {"threadId": tid})["stackFrames"]; R["B after stepOut"] = (fr[0]["name"], fr[0].get("line"))
    e = req("evaluate", {"expression": "g", "frameId": fr[0]["id"], "context": "watch"}); R["B evaluate global g"] = e.get("result")
    req("continue", {"threadId": tid})
    R["A8 exit code"] = ev("exited")["body"]["exitCode"]
    send("disconnect")
except BaseException as ex:
    import traceback; R["ERROR"] = repr(ex) + " | " + traceback.format_exc()[-400:] + " | stderr: " + (p.stderr.read().decode(errors="replace")[:300] if p.poll() is not None else "")
if False: R["x"] = "adapter closed: " + p.stderr.read().decode(errors="replace")[:300]
for k, v in R.items(): print("%-36s %s" % (k, v), flush=True)
try: p.wait(5)
except Exception: p.kill()
os._exit(0)
