# -*- coding: utf-8 -*-
# Subtext's accurate-transcription runner (the engine-whisper pack, on top of engine-ct2): ivrit.ai's Whisper
# large-v3-turbo in CTranslate2 int8, through faster-whisper. Run by the EMBEDDED Python of engine-ct2:
#
#   ..\engine-ct2\py\python.exe -I -s engine-whisper\stt_runner.py <model dir> <threads>
#
# Protocol - one JSON object per line on stdin; on stdout, JSON lines:
#   {"id":n,"wav":"<16 kHz mono WAV>","hot":"<words to prefer, optional>"}
#     -> {"id":n,"seg":[start,end,"text",[["word",start,end,prob],...]]}   one per segment, as soon as it is ready
#     -> {"id":n,"done":true,"sec":<audio seconds>}
#   a failure -> {"id":n,"error":"..."}
# Prints {"ready":true} once the model is loaded. Paths are relative to the working directory: the C++ libraries
# break on a folder name with Hebrew letters, so the app starts this from the models folder.
# Settings measured 22.11 (docs/RESEARCH-local-models.md 3b): greedy (beam 1) - as accurate as beam 5 here and
# faster; no conditioning on the previous text (fewer repetition loops); Hebrew always (the fine-tune lost
# language detection - its model card says to set it).
import sys, os, json
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "site"))
sys.stdout.reconfigure(encoding="utf-8")
sys.stdin.reconfigure(encoding="utf-8")
import wave
import numpy as np
from faster_whisper import WhisperModel

d = sys.argv[1]
th = int(sys.argv[2])
model = WhisperModel(d, device="cpu", compute_type="int8", cpu_threads=th, num_workers=1)


def read_wav(path):
    with wave.open(path, "rb") as w:
        if w.getsampwidth() != 2 or w.getnchannels() != 1 or w.getframerate() != 16000:
            raise ValueError("expected 16 kHz mono 16-bit WAV")
        a = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16)
    return a.astype(np.float32) / 32768.0


print(json.dumps({"ready": True}), flush=True)
for line in sys.stdin:
    line = line.strip().lstrip("\ufeff")
    if not line:
        continue
    req = None
    try:
        req = json.loads(line)
        rid = req.get("id")
        audio = read_wav(req["wav"])
        hot = (req.get("hot") or "").strip() or None
        segs, info = model.transcribe(audio, language="he", task="transcribe", beam_size=1, best_of=1,
                                      condition_on_previous_text=False, word_timestamps=True,
                                      vad_filter=False, hotwords=hot)
        for s in segs:
            words = [[w.word, round(w.start, 3), round(w.end, 3), round(w.probability, 4)] for w in (s.words or [])]
            print(json.dumps({"id": rid, "seg": [round(s.start, 3), round(s.end, 3), s.text, words]}, ensure_ascii=False), flush=True)
        print(json.dumps({"id": rid, "done": True, "sec": round(len(audio) / 16000.0, 3)}), flush=True)
    except Exception as ex:
        print(json.dumps({"id": req.get("id") if isinstance(req, dict) else None, "error": "%s: %s" % (type(ex).__name__, ex)}), flush=True)
