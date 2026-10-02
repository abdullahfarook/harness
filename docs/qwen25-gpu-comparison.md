# Qwen2.5-1.5B-Instruct ONNX GPU comparison

**Status: in progress.** CPU baseline measured; GPU candidates are not yet validated. Do not treat pending rows as successful or as speedup claims. All existing model classes and downloaded weights are retained.

## Hardware and method

RTX 4050 Laptop GPU, 6141 MiB VRAM; i5-13420H; approximately 24 GB RAM; SSD. Device memory at the start of this comparison: 600 MiB used, 5321 MiB free. This is an instantaneous whole-device sample, not model allocation or a guaranteed available budget.

Native C#/.NET runtime, Microsoft Agent Harness, native Laya and headed Playwright with red cursor. Original Q4 CPU generation uses greedy decoding and repetition penalty 1.1. New precision variants will be separate pinned, hash-verified bundles; no model is deleted or overwritten. Python is allowed only for offline graph/model preparation, not bot inference.

Fresh CPU measurements use the current shared summary prompt and guards, not historical timings from a different prompt/build. Benchmark serial inference; record tokens, retries and failures. GPU memory measurements must disclose WDDM/whole-device limitations.

## Results so far

| Candidate | Arithmetic mean (s) | Summary-only (s) | Full live including startup (s) | Status |
|---|---:|---:|---:|---|
| Original Q4 CPU | 1.866 | 123.195 | 186.284 | Measured and manually grounded |
| Q4F16 CUDA | — | — | — | Downloaded and hash-verified; native compatibility and benchmarks pending |
| FP16 CUDA | — | — | — | Pending; must fit without offloading |
| GenAI INT4 CUDA | — | — | — | Pending compatible export and native validation |

### CPU arithmetic

Five correct **42** answers. Startup 9.308 s; trials 2.123 / 1.849 / 1.739 / 1.782 / 1.836 s. One generated answer token; this is not representative of all simple questions. Evidence: artifacts/website-summary/qwen25-cpu-simple-01.

### CPU live OpenPlateStudio

Startup 15.731 s, Harness live execution 170.451 s, total 186.284 s. Actual navigation and classification decisions: 11.211 s and 8.770 s; one real Laya call. Summary-only 123.195 s, no decision or summary retries. Input 1531 tokens, output 207 tokens; generation timing includes prefill (44.069 s). This first baseline does not yet separate first-token sampling from prefill or decode-only throughput.

Manual review against captured text confirms the enterprise application boilerplate, developer audience, modular monolith/DDD, GraphQL/REST, AI/RAG, jobs/event bus, tenant isolation and TypeScript claims. Exact source and COMING SOON retained. Screenshot shows the red cursor. Output is more verbose than the requested four brief bullets. This summarizes the site's claims, not verified product availability/capabilities. Evidence: artifacts/website-summary/qwen25-cpu-live-01, including separate grounding-review.json.

## Validation so far

- Before changes: 94 ordinary regressions passed; explicit real-model tests are separate and not covered by that result.
- Runtime selection: three new RED failures (wrong default, unsupported selector, missing model identity), then 38 related tests passed after implementation.
- Precision assets: two RED failures because variants were not recognized; both then passed after implementation.
- Native FP16/FP32 tensor handling, placement checks, first/last-token metrics and both GPU-backend contracts were developed RED-to-GREEN.
- Latest ordinary regression suite: **113/113 passed** (Debug, explicit CPU-only build), 14 seconds. This is not proof of GPU inference; real-model checks remain separate.
- Initial isolated-build experiment aborted because a relative MSBuild extensions path omitted test runtime dependencies. Corrected to an absolute per-project path in the ignored cpu-debug.props file; the rerun produced the expected RED failures and subsequent GREEN results.

## Remaining before selecting a GPU default

CUDA/cuDNN verification; actual operator placement and device KV cache; Q4F16 and FP16 comparisons; compatible GenAI export; longer questions, replayed identical summary input, live captures, 4K/8K memory tests, thread sweep; full regression/real-model validation and review. No winner chosen yet.

## Primary references

- [Community model variants](https://huggingface.co/onnx-community/Qwen2.5-1.5B-Instruct/tree/main/onnx), revision 6287331f475a3e20e8c879be8fd4bf3551ad9d34.
- [ORT CUDA provider and compatibility](https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html).
- [C# OrtValue API](https://onnxruntime.ai/docs/tutorials/csharp/basic_csharp.html) and [device I/O binding](https://onnxruntime.ai/docs/performance/tune-performance/iobinding.html).
- [Offline graph optimization](https://onnxruntime.ai/docs/performance/model-optimizations/graph-optimizations.html).
- [GenAI CUDA model builder](https://github.com/microsoft/onnxruntime-genai/blob/main/src/python/py/models/README.md) and [past/present shared buffers](https://onnxruntime.ai/docs/genai/howto/past-present-share-buffer.html).

### Preparation failures retained

The first Q4F16 transfer ended with curl error 56 (TLS connection closed without close_notify) after 280,491,598 bytes. The terminal failure was confirmed, the partial was preserved, and a new setup invocation resumed it. The completed graph is 1,221,878,940 bytes, with a pinned, hash-verified manifest. This was a download failure, not an inference result. FP16 and CUDA-library transfers are still running.

GenAI will use a separate export from official checkpoint revision 989aa7980e4cf806f80c7fef2b1adb7bc71aa306, with native packages GenAI 0.17.1 and ORT GPU 1.30.0. Native GenAI repetition penalty operates on its full sequence; standard ORT sampling here penalizes only generated answer tokens. This difference must be disclosed when comparing output lengths and quality.
