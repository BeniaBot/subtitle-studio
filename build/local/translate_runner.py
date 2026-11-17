# -*- coding: utf-8 -*-
# Subtext's translation runner (the engine-ct2 pack): a CTranslate2 model with SentencePiece, and English
# punctuation with sherpa-onnx when the model folder has it. Run by the EMBEDDED Python next to it:
#
#   python -I -s translate_runner.py <model dir> <threads> [beam]
#
# Protocol - one JSON object per line on stdin, one JSON line back on stdout:
#   {"id":n,"texts":[...]}  ->  {"id":n,"out":[...]}     translation, same length
#   {"id":n,"punct":[...]}  ->  {"id":n,"out":[...]}     punctuation and case (punct-en.int8.onnx + punct-en.vocab
#                                                         in the model dir; loaded on the first request)
#   a failure               ->  {"id":n,"error":"..."}
# Prints {"ready":true} once the translation model is loaded. Paths are relative to the working directory:
# the C++ libraries break on a folder name with Hebrew letters, so the app starts this from the models folder.
import sys, json, os
sys.stdout.reconfigure(encoding="utf-8")
sys.stdin.reconfigure(encoding="utf-8")
import ctranslate2, sentencepiece as spm

d = sys.argv[1]
th = int(sys.argv[2])
beam = int(sys.argv[3]) if len(sys.argv) > 3 else 4
sp_s = spm.SentencePieceProcessor(model_file=os.path.join(d, "src.spm.model"))
sp_t = spm.SentencePieceProcessor(model_file=os.path.join(d, "tgt.spm.model"))
tr = ctranslate2.Translator(d, device="cpu", inter_threads=1, intra_threads=th, compute_type="int8")
_punct = None


def punctuator():
    global _punct
    if _punct is None:
        m = os.path.join(d, "punct-en.int8.onnx")
        v = os.path.join(d, "punct-en.vocab")
        if not (os.path.exists(m) and os.path.exists(v)):
            raise RuntimeError("no punctuation model in " + d)
        import sherpa_onnx
        _punct = sherpa_onnx.OnlinePunctuation(sherpa_onnx.OnlinePunctuationConfig(
            model_config=sherpa_onnx.OnlinePunctuationModelConfig(cnn_bilstm=m, bpe_vocab=v, num_threads=1)))
    return _punct


print(json.dumps({"ready": True}), flush=True)
for line in sys.stdin:
    line = line.strip().lstrip("﻿")
    if not line:
        continue
    req = None
    try:
        req = json.loads(line)
        if "punct" in req:
            p = punctuator()
            out = [p.add_punctuation_with_case(t) if t.strip() else t for t in req["punct"]]
        else:
            texts = req.get("texts", [])
            toks = [sp_s.encode(t, out_type=str) for t in texts]
            res = tr.translate_batch(toks, beam_size=beam, max_decoding_length=256) if toks else []
            out = [sp_t.decode(r.hypotheses[0]) for r in res]
        print(json.dumps({"id": req.get("id"), "out": out}, ensure_ascii=False), flush=True)
    except Exception as ex:
        print(json.dumps({"id": req.get("id") if isinstance(req, dict) else None, "error": str(ex)}), flush=True)
