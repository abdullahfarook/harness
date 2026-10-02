# Qwen2.5-1.5B-Instruct ONNX GPU comparison

**Status: in progress.** CPU baseline and two ORT CUDA candidates measured (Q4F16 complete for arithmetic, live and replay; FP16 arithmetic only, then out of memory); GenAI INT4 not yet run. Results are single runs on one prompt/site; do not treat them as general speedup claims. All existing model classes and downloaded weights are retained.

## Hardware and method

RTX 4050 Laptop GPU, 6141 MiB VRAM; i5-13420H; approximately 24 GB RAM; SSD. Device memory at the start of this comparison: 600 MiB used, 5321 MiB free. This is an instantaneous whole-device sample, not model allocation or a guaranteed available budget.

Native C#/.NET runtime, Microsoft Agent Harness, native Laya and headed Playwright with red cursor. Original Q4 CPU generation uses greedy decoding and repetition penalty 1.1. New precision variants will be separate pinned, hash-verified bundles; no model is deleted or overwritten. Python is allowed only for offline graph/model preparation, not bot inference.

Fresh CPU measurements use the current shared summary prompt and guards, not historical timings from a different prompt/build. Benchmark serial inference; record tokens, retries and failures. GPU memory measurements must disclose WDDM/whole-device limitations.

## Results so far

| Candidate | Arithmetic mean (s) | Summary-only (s) | Full live including startup (s) | Status |
|---|---:|---:|---:|---|
| Original Q4 CPU | 1.866 | 123.195 | 186.284 | Measured and manually grounded |
| Q4F16 CUDA | 0.236 | 4.843 (replay of captured input, mean of 3) | 31.869 | Measured; GPU placement check passed; summary byte-identical to the CPU summary |
| FP16 CUDA | 0.464 | — | — (failed) | Arithmetic measured; live and replay summaries fail with a CUDA out-of-memory error under the 5 GiB cap |
| GenAI INT4 CUDA | — | — | — | Not run: the pinned checkpoint download stalled, so the export has not been produced |

Arithmetic and replay are not directly comparable to the CPU column in every respect: the replay figure is a captured-input replay (no browsing, no Laya), whereas the CPU summary-only figure (123.195 s) came from the live run. A like-for-like CPU replay has not been run yet.

### CPU arithmetic

Five correct **42** answers. Startup 9.308 s; trials 2.123 / 1.849 / 1.739 / 1.782 / 1.836 s. One generated answer token; this is not representative of all simple questions. Evidence: artifacts/website-summary/qwen25-cpu-simple-01.

### CPU live OpenPlateStudio

Startup 15.731 s, Harness live execution 170.451 s, total 186.284 s. Actual navigation and classification decisions: 11.211 s and 8.770 s; one real Laya call. Summary-only 123.195 s, no decision or summary retries. Input 1531 tokens, output 207 tokens; generation timing includes prefill (44.069 s). This first baseline does not yet separate first-token sampling from prefill or decode-only throughput.

Manual review against captured text confirms the enterprise application boilerplate, developer audience, modular monolith/DDD, GraphQL/REST, AI/RAG, jobs/event bus, tenant isolation and TypeScript claims. Exact source and COMING SOON retained. Screenshot shows the red cursor. Output is more verbose than the requested four brief bullets. This summarizes the site's claims, not verified product availability/capabilities. Evidence: artifacts/website-summary/qwen25-cpu-live-01, including separate grounding-review.json.

### GPU arithmetic (same prompt, 5 trials, greedy)

| Candidate | Startup (s) | Trials (s) | Mean (s) | Answer | Evidence |
|---|---:|---|---:|---|---|
| Original Q4 CPU | 9.308 | 2.123 / 1.849 / 1.739 / 1.782 / 1.836 | 1.866 | `42` | qwen25-cpu-simple-01 |
| Q4F16 CUDA | 8.265 | 0.723 / 0.105 / 0.112 / 0.119 / 0.118 | 0.236 | `42` (5/5) | qwen25-q4f16-cuda-simple-01 |
| FP16 CUDA | 10.224 | 0.928 / 0.375 / 0.363 / 0.326 / 0.329 | 0.464 | `6 times 7 is 42.` (5/5) | qwen25-fp16-cuda-simple-01 |

The first GPU trial includes warm-up; the mean includes it. Steady-state trials are about 0.11 s (Q4F16) and 0.33-0.38 s (FP16). Q4F16 produces one token, FP16 a full sentence, so the means are not equal work. Input is 40 tokens, so this says little about long prompts.

### GPU live OpenPlateStudio (headed, red cursor)

Q4F16 CUDA (qwen25-q4f16-cuda-live-02): startup 12.340 s, live execution 19.441 s, total 31.869 s, one real Laya call. Generation timings:

| Call | Input tokens | Output tokens | Prefill (s) | Generation (s) | Decode tok/s |
|---|---:|---:|---:|---:|---:|
| Navigation decision | 218 | 17 | 1.028 | 1.732 | 26.8 |
| Classification decision | 230 | 9 | 0.259 | 0.590 | 27.3 |
| Summary | 1531 | 207 | 1.172 | 9.163 | 25.9 |

The summary is byte-identical to the CPU summary (134 words) and was not re-reviewed for grounding separately; the earlier grounding review of the CPU text therefore applies to the same text. Source: this is a summary of the site's claims, not verified product capability.

FP16 CUDA (qwen25-fp16-cuda-live-02) did not complete. The two decision calls ran (218 -> 17 tokens, 0.783 s prefill; 230 -> 9 tokens) and one 1531-token generation was recorded (218 output tokens, 1.047 s prefill, 7.671 s generation, 32.9 tok/s), but the run then aborted before writing run.json with:

> Non-zero status code returned while running Cast node 'graph_output_cast0' ... BFCArena::AllocateRawInternal Available memory of 34699008 is smaller than requested bytes of 945041920

Inference, not verified: 945,041,920 bytes is about 1554 tokens x 151,936 vocabulary x 4 bytes, i.e. the full-sequence float32 logits produced at prefill, which this model graph emits for every input position. The FP16 weights are about 3.1 GB, so with the 5 GiB arena cap there is little room left for these logits plus the KV cache. I did not determine which call exceeded it. The same error occurred in the replay run (qwen25-fp16-cuda-replay-01), so it is reproducible and not a one-off. FP16 is therefore not viable at this cap for long prompts as currently implemented; I have not tried a higher cap, a smaller context, or last-token-only logits.

The first attempt at both live runs (`-01`) failed for an unrelated reason, retained as evidence: ORT profile files were written to the repository root and the placement check could not read a profile that was still open.

### GPU replay of the captured OpenPlateStudio input (3 trials)

Q4F16 CUDA: startup 5.222 s; trials 5.154 / 4.615 / 4.760 s (mean 4.843 s). All three outputs are byte-identical to the CPU summary. FP16 CUDA failed with the out-of-memory error above. The 4.6-5.2 s replay time is shorter than the 9.163 s summary generation in the live run for identical output; I have not explained that difference (warm-up and the live run's first-use cost are plausible but untested).

### Placement evidence

Q4F16 and FP16 runs wrote cuda-placement.json: KV cache outputs are CUDA-resident, and the first-prefill profile shows no CPU transformer compute and no large CPU payloads. Logits are returned to the CPU and the small inputs are copied at binding; the cache is dynamic device past/present, not an in-place shared buffer. WDDM whole-device memory samples (358 MiB to 1013 MiB idle) are not model allocation figures.

## Validation so far

- Before changes: 94 ordinary regressions passed; explicit real-model tests are separate and not covered by that result.
- Runtime selection: three new RED failures (wrong default, unsupported selector, missing model identity), then 38 related tests passed after implementation.
- Precision assets: two RED failures because variants were not recognized; both then passed after implementation.
- Native FP16/FP32 tensor handling, placement checks, first/last-token metrics and both GPU-backend contracts were developed RED-to-GREEN.
- Latest ordinary regression suite: **113/113 passed** (Debug, explicit CPU-only build), 14 seconds. This is not proof of GPU inference; real-model checks remain separate.
- After the 113/113 run, two small changes were made (profile prefix ordering in the CUDA session options, and shared-read access when the placement check opens a profile). The regression suite has **not** been re-run since; only the real-model GPU runs above exercised them.
- Initial isolated-build experiment aborted because a relative MSBuild extensions path omitted test runtime dependencies. Corrected to an absolute per-project path in the ignored cpu-debug.props file; the rerun produced the expected RED failures and subsequent GREEN results.

## Remaining before selecting a GPU default

Done: cuDNN 9 / cuBLAS installed into the repository-local export environment; operator placement and device KV cache checked for Q4F16 and FP16; arithmetic, live and replay runs for Q4F16; arithmetic for FP16.

Still open: compatible GenAI export and native run; a like-for-like CPU replay; longer questions; 4K/8K memory tests; thread sweep; a decision on FP16 (higher arena cap, smaller context, or last-token logits); regression re-run, real-model tests and review. No winner is chosen yet. Q4F16 CUDA is the only GPU candidate with a complete live result so far, and in these runs it was about 8x faster than CPU on the arithmetic prompt and finished the live run in 31.9 s versus 186.3 s, with an identical summary. These are single runs on one prompt/site, not a statistical claim.

## Primary references

- [Community model variants](https://huggingface.co/onnx-community/Qwen2.5-1.5B-Instruct/tree/main/onnx), revision 6287331f475a3e20e8c879be8fd4bf3551ad9d34.
- [ORT CUDA provider and compatibility](https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html).
- [C# OrtValue API](https://onnxruntime.ai/docs/tutorials/csharp/basic_csharp.html) and [device I/O binding](https://onnxruntime.ai/docs/performance/tune-performance/iobinding.html).
- [Offline graph optimization](https://onnxruntime.ai/docs/performance/model-optimizations/graph-optimizations.html).
- [GenAI CUDA model builder](https://github.com/microsoft/onnxruntime-genai/blob/main/src/python/py/models/README.md) and [past/present shared buffers](https://onnxruntime.ai/docs/genai/howto/past-present-share-buffer.html).

### Preparation failures retained

The first Q4F16 transfer ended with curl error 56 (TLS connection closed without close_notify) after 280,491,598 bytes. The terminal failure was confirmed, the partial was preserved, and a new setup invocation resumed it. The completed graph is 1,221,878,940 bytes, with a pinned, hash-verified manifest. This was a download failure, not an inference result. The FP16 bundle (3,104,177,152-byte data file) later completed with a hash-verified manifest. The first CUDA-library install into the export environment was interrupted and left no cuDNN; it was re-run and completed (cuDNN 9.27, cuBLAS 13.8, ORT GPU 1.30.0, GenAI CUDA 0.17.1).

The GenAI source checkpoint download via huggingface_hub stalled on model.safetensors (a 43 KB incomplete file that did not grow over 20 s) and was stopped. A resumable curl retry was not run. Tokenizer and config files at the pinned revision are present in the ignored work folder; the GenAI INT4 export has not been produced.

GenAI will use a separate export from official checkpoint revision 989aa7980e4cf806f80c7fef2b1adb7bc71aa306, with native packages GenAI 0.17.1 and ORT GPU 1.30.0. Native GenAI repetition penalty operates on its full sequence; standard ORT sampling here penalizes only generated answer tokens. This difference must be disclosed when comparing output lengths and quality.
