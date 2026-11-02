"""Send a prompt to Ollama on the EVO and append token usage to usage.jsonl.

Usage:  python ollama_ask.py [-m MODEL] [-s SYSTEM_FILE] [-f PROMPT_FILE] [prompt...]
Cloud (default): glm-5.3:cloud (code), glm-5.3-flash:cloud (fast), kimi-k2.7-code:cloud, minimax-m3:cloud, gpt-oss:120b-cloud.
Local fallback: qwen3.8:27b, qwen3.5:9b. Thinking is kept out of the answer (returned separately by the API).
"""
import argparse, json, sys, time, urllib.request, pathlib, datetime

import os
HOST = os.environ.get("OLLAMA_HOST_URL", "http://localhost:11434")
LOG = pathlib.Path(__file__).with_name("usage.jsonl")

def ask(prompt, model, system=None, think=True):
    body = {"model": model, "prompt": prompt, "stream": False, "think": think,
            "options": {"num_ctx": 16384, "temperature": 0.2}}
    if system:
        body["system"] = system
    req = urllib.request.Request(f"{HOST}/api/generate", data=json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json"})
    t0 = time.time()
    with urllib.request.urlopen(req, timeout=900) as r:
        res = json.load(r)
    rec = {"ts": datetime.datetime.now().isoformat(timespec="seconds"), "provider": "ollama-evo",
           "model": model, "prompt_tokens": res.get("prompt_eval_count"),
           "output_tokens": res.get("eval_count"), "seconds": round(time.time() - t0, 1),
           "think_tokens_included": bool(res.get("thinking")),
           "tok_per_s": round(res.get("eval_count", 0) / max(res.get("eval_duration", 0) / 1e9 or (time.time() - t0), 0.001), 1)}
    with LOG.open("a", encoding="utf-8") as f:
        f.write(json.dumps(rec) + "\n")
    return res["response"], rec

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("-m", "--model", default="glm-5.3:cloud")
    ap.add_argument("-s", "--system")
    ap.add_argument("-f", "--file")
    ap.add_argument("--no-think", action="store_true")
    ap.add_argument("prompt", nargs="*")
    a = ap.parse_args()
    prompt = pathlib.Path(a.file).read_text(encoding="utf-8") if a.file else " ".join(a.prompt)
    system = pathlib.Path(a.system).read_text(encoding="utf-8") if a.system else None
    text, rec = ask(prompt, a.model, system, not a.no_think)
    sys.stdout.write(text + "\n")
    sys.stderr.write(f"[usage] {json.dumps(rec)}\n")
