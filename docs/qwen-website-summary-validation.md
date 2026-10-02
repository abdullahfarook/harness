# Qwen2.5-1.5B-Instruct ONNX validation — 2026-10-02

**Historical Qwen2.5 baseline at commit `3d96211`.** The default is now Qwen3; use the [current Qwen3 report](qwen3-website-summary-validation.md). Commands below describe the historical version.

## Setup and repeat checks

Windows x64, .NET 10, PowerShell and curl.exe; run from repository root:

```powershell
pwsh -File scripts/Setup-WebsiteBot.ps1 -Brain qwen
dotnet build harness -c Release
dotnet test tests/Harness.Tests -c Release --filter "TestCategory!=ModelIntegration"
# Every explicit real-model regression, including the earlier Gemma/LFM adapters:
dotnet test tests/Harness.Tests -c Release --filter "FullyQualifiedName~RealInference|FullyQualifiedName~LayaTypedParity"
dotnet run --project harness -c Release -- --question "What is 6 times 7? Answer briefly." --repeats 3 --output artifacts/website-summary/qwen-simple-new
dotnet run --project harness -c Release -- --url https://openplatestudio.com/ --output artifacts/website-summary/qwen-live-new --keep-open-seconds 5
```

The all-model regression command also needs the older weights; use `Setup-WebsiteBot.ps1 -Brain all` if not already downloaded. Use fresh output directories and do not run competing inference or build the same configuration while it executes. Override Qwen with `--qwen-model` / `QWEN_MODEL_PATH`; Laya with `--laya-model` / `LAYA_MODEL_PATH`. Default browser is headed with an animated red cursor. `--headless` is optional; `--timeout-seconds` defaults to 1800.

## Exact runtime

- [onnx-community/Qwen2.5-1.5B-Instruct](https://huggingface.co/onnx-community/Qwen2.5-1.5B-Instruct), revision `6287331f475a3e20e8c879be8fd4bf3551ad9d34`; Q4 ONNX graph **1,787,566,590 bytes**. Setup and runtime validate identity/revision/required files, sizes and SHA256.
- Native C# ONNX Runtime 1.30.0 and Hugging Face tokenizer; no Python/PyTorch inference, GGUF, external model service or cluster changes. CPU: Intel i5-13420H, four inference threads, one generation at a time.
- Greedy decoding with repetition penalty 1.1, matching the pinned export's penalty setting. Native Qwen chat delimiters/default system prompt; EOS IDs 151645/151643; cached attention across 28 layers, two KV heads of dimension 128. Application input+output budget: 8192 tokens. No thinking section or forced reasoning closure.
- Actual Microsoft Agent Harness still invokes the browser and native Laya tools. Qwen chooses validated JSON actions and generates the answer. The LFM class supplies only a static numeric token-selection helper—not LFM weights or inference.
- Existing private-network/read-only/same-origin restrictions, page/action limits, blocked redirects/WebSockets/downloads/service workers, structural-token neutralization and source/COMING SOON guards remain active. DNS prechecks are not a hardened network sandbox; grounding remains manually reviewed.

## Measured timings

Simple question: three correct `42` answers.

| Measurement | Seconds |
|---|---:|
| Asset verification/model startup | 16.86 |
| Question trial 1 | 2.15 |
| Question trial 2 | 2.71 |
| Question trial 3 | 2.55 |
| Mean question inference | **2.47** |
| Startup + first answer | 19.00 |

Evidence: `artifacts/website-summary/qwen-simple-01/question.json` and `events.jsonl`.

Final-build repeat (`qwen-simple-02`), again three correct `42` answers: startup **7.38 s**, answer calls **0.926 / 0.787 / 0.768 s**, mean **0.827 s**, startup + first answer **8.30 s**. Both measured runs are retained: answer-call range across all six trials is **0.77–2.71 s**. This variation is not a guaranteed latency; disk cache, CPU state and other machine conditions were not experimentally controlled. The simple arithmetic answer is only one generated token, not representative of every simple question.

Successful headed live run: `artifacts/website-summary/qwen-live-03`, exit code 0, **no action or summary retries**:

| Live measurement | Seconds |
|---|---:|
| Model verification/loading + browser startup | 15.96 |
| Qwen navigation decision | 5.60 |
| Qwen classification decision | 4.88 |
| **Summary generation only (including prefill)** | **72.21 (1 min 12 s)** |
| Summary prefill | 24.28 |
| Summary decoding | 47.93 |
| Live Harness execution, excluding startup | 87.70 |
| **Total startup + live run** | **103.76 (1 min 44 s)** |

Summary input: 1508 tokens; output: 183 tokens. Qwen selected both actions; Harness really navigated and invoked native Laya once (512 tokens, relevance probability 0.867908). The final run had no rejected attempts; earlier failed runs are listed below and excluded from this individual successful-run timing—not hidden as successful inference.

Timing fields follow the Gemma report: model-response `seconds` covers each inference; `GenerationSeconds` includes prefill (not decode alone). `run.json` separates full startup, live Harness execution and total. Download/build and final browser keep-open window are excluded. Any retries are included in reported summary total.

Previous measurements: Gemma question mean 3.83 seconds, successful summary inference 104 seconds, summary including retries 357.54 seconds, entire live run 551.80 seconds. LFM arithmetic ~62 seconds included test setup, final summary ~98 seconds and entire live run ~194 seconds. These are observed configurations, not controlled statistical benchmarks; output sizes, optional thinking and retries differ.

## Validation

The initial regression suite passed 61/61 before live diagnosis; final results follow below. Covers model asset identity, tokenization/cache/prompt logic, typed probabilities, JSON/native protocols, readiness and final guards, concurrent evidence writes, CLI settings, private-network/resource rejection, redirects/WebSockets, empty pages, cancellation, headed mouse motion/navigation, stale links and limits. Fresh review found a missing runtime revision check; two red-to-green regressions now enforce the Qwen pin.

Final build: zero warnings/errors. Final regression suite: **62/62 passed**, 13 seconds. All **5/5 explicit real-model tests passed**, 51 seconds: Qwen, Gemma and LFM actual generation, Laya actual inference and independent-package typed-output parity. No skipped tests in either run. Real test results: `tests/Harness.Tests/TestResults/qwen-all-real.trx` (Git-ignored).

### Grounding review

Compared the actual `summary.txt` with `page-00.json` and inspected `page-00.png`: product is an enterprise application boilerplate; audience is developers/engineering teams; modular monolith/DDD, generated GraphQL/REST, AI scaffolding/streaming/RAG, jobs, event bus, tenant filters and OpenIddict all have captured support. COMING SOON and the exact observed source URL are preserved. Placeholder docs/pricing links were not represented as verified destinations. The screenshot shows the red cursor, and the capture records 20 moves.

`grounding-review.json` separately records manual acceptance; `run.json` intentionally retains `generated-awaiting-grounding-review`. This is a summary of the website's claims, not a product capability/reliability audit. Qwen formatted four category sections with nested feature bullets rather than exactly four bullets. Laya reads its existing bounded title/body input (2500 body characters, tokenizer capped at 512); the summarizer receives the complete captured body.

Fresh read-only review found no important routing/cache/protocol issue after the revision fix. Logs: `.local/website-bot-work/qwen-final-build.log`, `qwen-final-unit.log`, `qwen-all-real.log`, `qwen-live-03.log`; provenance and timings are in `models.json`, `events.jsonl`, `run.json`. Weights and generated run evidence remain Git-ignored. No Kubernetes changes; unrelated staged documents remain untouched.

### Live failures and fixes (not passing runs)

- `qwen-live-01`: correct native model-selected navigation and actual headed capture, but classification copied the captured body into an unsupported `page_content` argument. Generation reached the 1536-token limit without EOS and failed closed; no Laya result or summary was accepted.
- `qwen-diagnostic-01`: decoded partial output confirmed the same page-copying behavior; stopped the verified diagnostic process after that evidence, not an observation timeout.
- `qwen-live-02`: a receipt-only classification routing prompt eliminated the long page echo, but Qwen still emitted `page_content: ""` in all three bounded attempts. Argument validation rejected every attempt; no fabricated classifier/summary fallback.
- Added a red-to-green prompt regression: routing omits the body and the generic parameter/value example; final summary still receives the original body. For the zero-argument readiness step, expose only its exact empty-object action format. Actual Qwen inference still produces the action and Harness invokes Laya against stored evidence. No tool call is synthesized or silently repaired.
