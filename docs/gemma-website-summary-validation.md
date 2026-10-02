# Gemma 4 E2B website-summary validation — 2026-10-02

**Historical Gemma baseline at commit `7bfd390`.** The default brain is now Qwen3; use the [current Qwen3 report](qwen3-website-summary-validation.md). The commands below describe the older Gemma version.

## Setup / repeat validation

Run in the repository root on Windows x64 with .NET 10, PowerShell and curl.exe:

```powershell
pwsh -File scripts/Setup-WebsiteBot.ps1 -Brain gemma
dotnet build harness -c Release
dotnet test tests/Harness.Tests -c Release --filter "TestCategory!=ModelIntegration"
dotnet test tests/Harness.Tests -c Release --filter "FullyQualifiedName~GemmaRealInference|FullyQualifiedName~LayaRealInference|FullyQualifiedName~LayaTypedParity"
dotnet run --project harness -c Release -- --question "What is 6 times 7? Answer briefly." --repeats 3 --output artifacts/website-summary/gemma-simple-new
dotnet run --project harness -c Release -- --url https://openplatestudio.com/ --output artifacts/website-summary/gemma-live-new --keep-open-seconds 5
```

Do not run competing inference during benchmarks, or build the same configuration while its DLL is executing. Choose fresh output directories. Override weights with `--gemma-model` or `GEMMA_MODEL_PATH`; Laya uses `--laya-model` / `LAYA_MODEL_PATH`. `--headless` is optional; default is headed Chromium with an animated red cursor. `--timeout-seconds` defaults to 1800.

## Exact configuration

- Requested repository: [onnx-community/gemma-4-E2B-it-ONNX](https://huggingface.co/onnx-community/gemma-4-E2B-it-ONNX), pinned revision `9f4bef82ea6e296bc69f8a2f5939f73af81b07a6`.
- Q4 text embedding and merged decoder ONNX graphs, including per-layer embeddings and KV cache reuse, via native Microsoft.ML.OnnxRuntime 1.30.0. Approximately 3.63 GB of external weight data. Asset identity, required files, sizes and SHA256 are verified before loading.
- Text only; no audio/vision graph, Python/PyTorch inference process, GGUF conversion or external LLM service. Same Windows machine: Intel Core i5-13420H, CPU execution, four inference threads, one model generation at a time, 8192 application token budget.
- Thinking disabled by omitting the optional `<|think|>` system token; greedy decoding (`do_sample=false`), EOS from the export generation config. No forced reasoning closure or repetition penalty is used for Gemma.
- Actual Microsoft Agent Harness 1.23.0 still invokes LFM-independent browser/Laya tools. Gemma chooses validated JSON actions; the adapter does not synthesize calls. Laya remains the previously validated native export.

## Timing definitions

`question.json` separates asset verification/model-loading startup from each simple-question call. `events.jsonl` records model responses with stage (`decision` or `summary`), attempt and elapsed seconds. `generation_timing` additionally records input/output tokens and prefill time. Its `GenerationSeconds` means **total inference including prefill**, not decode-only time; subtract `PrefillSeconds` for decode time.

`run.json` separates complete startup (both models + browser), live Harness execution and their total. Final summary-step time includes processing the captured page and generating its answer, but excludes startup, browsing and earlier decisions/classification. Model downloads and build time are excluded. The final browser inspection window is also excluded from recorded run totals.

## Measured results

Simple question: all three answers were exactly `42`.

| Measurement | Seconds |
|---|---:|
| Simple-question startup | 16.05 |
| Question trial 1 | 4.22 |
| Question trial 2 | 2.87 |
| Question trial 3 | 4.41 |
| Mean question inference | 3.83 |
| Startup + first answer | 20.27 |

Evidence: `artifacts/website-summary/gemma-simple-01/question.json` and `events.jsonl`.

The real headed live run completed successfully (exit code 0), with two summary-validation retries:

| Live measurement | Seconds |
|---|---:|
| Startup: both models + browser | 28.52 |
| Navigation decision inference | 26.96 |
| Classification decision inference | 126.91 |
| First summary attempt (rejected) | 98.96 |
| Second summary attempt (rejected) | 154.53 |
| Third summary attempt (accepted) | 104.06 |
| All summary inference, including retries | **357.54 (5 min 58 s)** |
| Live Harness execution, excluding startup | 523.18 |
| Total startup + live run | **551.80 (9 min 12 s)** |

The first summary lacked the source URL and substituted the page's free-building CTA for its COMING SOON status. The second retained the URL but still missed the status. The guard rejected both. The final model-generated correction preserved COMING SOON explicitly. Retries are included in total summary time, not hidden. For comparison, the final accepted Gemma inference was ~104 seconds vs the earlier LFM final step ~98 seconds. **Gemma was faster for arithmetic but not for this full website workflow.**

Long-input processing was expensive: classification prefill alone took 120.83 seconds for 2152 tokens. Summary attempts used 1562–1621 input tokens and 97–130 generated tokens. The accepted attempt spent 32.22 seconds in prefill and ~71.84 seconds in decoding. These are individual observations, not a stable throughput benchmark.

The earlier LFM configuration measured ~62 seconds for its arithmetic test (including setup), ~194 seconds for the full website run and ~98 seconds for final summarization alone. These are observed configurations, **not a controlled model-only benchmark**: LFM was thinking-enabled/bounded; Gemma thinking is disabled, token counts differ, and the old LFM step timing was inferred from evidence timestamps.

## Safety / review

The existing read-only/public HTTP checks, same-origin navigation, five-page/twenty-action limits, fail-closed redirects, blocked WebSockets/downloads/service workers and coming-soon/source-URL guards are unchanged. DNS prechecks are not a hardened network sandbox. Grounding still requires comparing generated claims with captured page JSON; model output is not assumed true merely because execution succeeded.

Template delimiters in supplied content are neutralized. Native chat, cache dimensions, Gemma JSON action translation, timing/provenance and premature-final rejection have regression tests. Fresh code review found no important issue; the timing field interpretation above addresses its minor clarification.

## Results and grounding

- Final regression suite: **56/56 passed**, 18.9 seconds. Real Gemma inference and both real Laya checks also passed.
- Actual event evidence shows Gemma selecting JSON `navigate` and `classify` actions, real Harness execution, native Laya probability **0.8679080604370757**, and 20 visible-cursor motion events. No canned answer or scripted action was substituted.
- Manual review of `artifacts/website-summary/gemma-live-01/summary.txt` against `page-00.json` supports every final claim: enterprise application boilerplate, modular monolith/DDD/APIs/scaffolding, engineers audience, multi-tenancy, OAuth/OpenID Connect, typed GraphQL, streamed AI/RAG, jobs, event bus, records/access features, and COMING SOON status. The source URL matches the observed page. Placeholder links were not treated as verified documentation or pricing.
- Screenshot inspected: `artifacts/website-summary/gemma-live-01/page-00.png`. Detailed timings/model provenance: `events.jsonl`, `models.json` and `run.json`. Accepted grounding review is recorded separately from the application's `generated-awaiting-grounding-review` status.
- Logs: `.local/website-bot-work/gemma-final-unit.log`, `gemma-real-tests.log`, `gemma-simple-01.log`, `gemma-live-01.log`. Large weights and run artifacts remain Git-ignored. Original staged cluster-validation documents and Kubernetes state were untouched.
