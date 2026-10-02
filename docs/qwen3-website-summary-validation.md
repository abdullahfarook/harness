# Qwen3-1.7B Q4 local website bot — 2026-10-02

**Historical Qwen3 baseline at commit `ccc49b7`.** The default is now Qwen3.5; see the [current report](qwen35-website-summary-validation.md). Commands below describe the prior version.

## Run

From the repository root on Windows x64 with .NET 10:

```powershell
pwsh -File scripts/Setup-WebsiteBot.ps1 -Brain qwen3
dotnet build harness -c Release
dotnet run --project harness -c Release -- --thinking off --url https://openplatestudio.com/ --output artifacts/website-summary/qwen3-off-new
dotnet run --project harness -c Release -- --thinking on --url https://openplatestudio.com/ --output artifacts/website-summary/qwen3-on-new
dotnet run --project harness -c Release -- --thinking off --question "What is 6 times 7? Answer briefly." --repeats 3 --output artifacts/website-summary/qwen3-question-new
dotnet test tests/Harness.Tests -c Release --filter "TestCategory!=ModelIntegration"
# Needs all older local weights; Setup-WebsiteBot.ps1 -Brain all restores them.
dotnet test tests/Harness.Tests -c Release --filter "FullyQualifiedName~RealInference|FullyQualifiedName~LayaTypedParity"
```

Thinking defaults to **off**. Use `--qwen3-model` / `QWEN3_MODEL_PATH` to override the directory, `--seed` (default 42) for reproducible sampling, and optional `--presence-penalty` (0..2, default 0). The official model advises 1.5 when significant repetition occurs; it is not applied universally. Soft thinking directives in supplied text are neutralized so the CLI controls the hard switch. Laya overrides remain unchanged. Default browser is headed with an animated red cursor; use fresh output directories and run inference serially.

## Runtime and protocol

- [Qwen3-1.7B](https://huggingface.co/Qwen/Qwen3-1.7B) is the post-trained, instruction-following model, not the separate Base checkpoint; its name does not need an Instruct suffix.
- [ONNX export](https://huggingface.co/onnx-community/Qwen3-1.7B-ONNX), pinned revision `cc6a06a21d614e9b8e92a6adfab1074d4e7d2438`; self-contained `onnx/model_q4.onnx`, **2,147,212,861 bytes**. Manifest identity/revision/file list, sizes and SHA256 are checked.
- Native C# ONNX Runtime 1.30.0, CPU Intel i5-13420H, four inference threads. Existing cached decoder is shared with Qwen2.5; Qwen3 uses actual graph metadata (28 layers, eight KV heads, head dimension 128). No Python/PyTorch model execution, remote inference or Kubernetes changes.
- Hard thinking-off template appends the export's empty thinking block to the assistant generation prefix. Thinking-on leaves that prefix open for native generation. No forced reasoning closure, guessed actions or canned summary fallback.
- Sampling: thinking-on temperature 0.6/top-p 0.95; off 0.7/0.8; both top-k 20. Not greedy decoding. The random generator is seeded once per process; later responses consume its continued sequence.
- Native tool signatures use the export's tools wrapper; output uses a single tool-call wrapper with JSON `name` and `arguments`. Strict parser translates validated calls into actual Microsoft Harness function calls; native Laya and Playwright execute them. Multiple/unknown/malformed calls, invalid parameters, incomplete/non-leading/nested thinking and legacy syntax fail closed. Historical thinking is not replayed.
- Output limits: thinking-on 4096 tokens per Harness response (2048 for questions); off 1536 (512 for questions). Input plus reserved output is limited to 8192 tokens. Up to three malformed-response/summary-validation attempts, existing overall timeout default 1800 seconds. Output-limit failure does not accept a partial answer.
- Existing readiness, bounded Laya title/body input, full captured-body summary evidence, exact observed source/COMING SOON guards and public/read-only browser restrictions remain. DNS prechecks are not a hardened network sandbox; generated claims still require manual grounding review.

## Measurement and validation

Startup includes asset verification/loading (and browser for live runs). Summary-only inference includes page prefill, generated thinking when enabled, and answer decoding, but not startup/navigation/classification. Output token counts include generated thinking. All accepted/rejected attempts must be included in summary total. Downloads/builds and the final browser keep-open window are excluded. Mode comparisons are individual observations, not controlled statistical benchmarks.

### Simple question (actual CPU generation, three trials each)

All six answers correctly reported 42 for `What is 6 times 7? Answer briefly.`:

| Measurement | Thinking off | Thinking on |
|---|---:|---:|
| Startup seconds | 10.36 | 10.11 |
| Trial 1 seconds | 2.72 | 48.55 |
| Trial 2 seconds | 1.67 | 36.61 |
| Trial 3 seconds | 1.64 | 36.40 |
| **Mean inference seconds** | **2.01** | **40.52** |

Evidence: `artifacts/website-summary/qwen3-simple-off-01/question.json` and `qwen3-simple-on-01/question.json`, plus their generation timings. Thinking is stripped from displayed answers, not bypassed during inference. Timing differences include different output lengths; one arithmetic question is not a universal speed guarantee.

### Live runs and final tests

Initial thinking-off live run completed successfully, exit 0, with **one summary retry**. First attempt omitted the source URL and was rejected. Second attempt preserved the URL and COMING SOON; no parser repair or canned fallback. Actual model-selected native navigation/classification executed through Harness, with one real Laya result (512 tokens, relevance 0.867908).

| Thinking-off live measurement | Seconds |
|---|---:|
| Startup | 25.31 |
| Navigation decision | 16.88 |
| Classification decision | 9.30 |
| Rejected summary attempt | 38.33 |
| Accepted summary attempt | 54.54 |
| **All summary inference including retry** | **92.87** |
| Live Harness execution excluding startup | 126.44 |
| **Startup + full live run** | **151.96 (2 min 32 s)** |

Summary attempts used 1512/1536 input tokens and 44/77 output tokens. Manual comparison of accepted `summary.txt` against `page-00.json` supports all product/audience/features/status claims. The inspected screenshot shows the red cursor; capture records 20 movements. Exact source URL is present, and placeholder links were not represented as verified docs/pricing. Formatting is category lines rather than exact bullet markers. Manual acceptance is recorded in `grounding-review.json`; application `run.json` remains `generated-awaiting-grounding-review` intentionally. Evidence: `artifacts/website-summary/qwen3-live-off-01`.

Initial thinking-on `qwen3-live-on-01` terminated with exit 0 but was **rejected in manual grounding**, not counted as a validated summary: it incorrectly called MODULAR MONOLITH an exact availability status. The displayed availability badge is COMING SOON; Modular Monolith is architecture. This run measured summary inference 168.01 s and full startup/live 313.83 s, with no automatic retries. Added a red-to-green guard for architecture incorrectly placed on the Availability line and clarified the summary prompt. Both modes are rerun after that fix; results and final all-model tests follow after actual completion.

Review also found two native parser gaps: legacy LFM syntax could be accepted when final output was allowed, and permissive thinking stripping could conceal multiple calls. Four reproducing regression cases failed before the fix and pass with strict single-leading-block parsing. These fixes do not synthesize or silently repair model actions.

### Final live validation after the grounding fix

Both actual headed runs terminated with exit 0 and passed manual grounding review. Native Qwen3 navigation/classification calls were accepted without action retries, and each run executed Laya once with relevance 0.867908/512 tokens.

| Measurement (seconds) | Thinking off | Thinking on |
|---|---:|---:|
| Startup | 23.01 | 14.24 |
| Navigation decision | 14.18 | 67.04 |
| Classification decision | 7.83 | 9.35 |
| Rejected summary inference | 38.33 | None |
| Accepted summary inference | 45.28 | 220.82 |
| **Summary-only total including all retries** | **83.61** | **220.82** |
| Summary retries | 1 | 0 |
| Live execution excluding startup | 110.56 | 303.55 |
| **Startup + full live run** | **133.72** | **317.95** |

Off attempts: 1535/1559 input tokens, 37/77 output tokens. The first omitted the source and described architecture rather than clearly identifying the boilerplate; source validation rejected it. Accepted off prefill was 24.85 s. On: 1531 input, 565 output tokens including thinking; prefill 50.40 s, generated-token decoding ~170.42 s. Nothing was force-closed to obtain these results.

Accepted summaries accurately identify the enterprise application boilerplate, developer/engineering audience, observed API/AI/architecture features and COMING SOON availability. No architecture is presented as an availability badge, and the exact source URL is preserved. Off additionally mentions captured tenant/OAuth/jobs/event-bus/queryable-module features. Inspected both headed screenshots and 20-move cursor evidence; placeholder links were not called verified destinations. Both final outputs use four category lines rather than literal bullet markers. `grounding-review.json` records manual acceptance separately from the application's provisional run status.

Evidence: `artifacts/website-summary/qwen3-live-off-02` and `qwen3-live-on-02`; models/provenance, per-attempt outputs/timings, captures and final summaries are retained. Earlier runs and rejected attempts above remain disclosed rather than cherry-picked out. Compared with the prior Qwen2.5 observation (72.21 s summary/no retry; 103.76 s full), these Qwen3 observations did **not** improve the live workflow's speed. Output sizes, sampling, retries and machine state differ, so this is not a controlled model-only benchmark.

### Final verification

- Release build: **0 warnings, 0 errors**, exit 0 (the separate runtime folding warnings above remain disclosed).
- Regression suite: **79/79 passed**, no skipped tests, 15 seconds.
- All explicit real-model tests: **7/7 passed**, no skipped tests, 2 min 40 seconds. Includes Qwen3 real arithmetic and native validated tool calls with thinking off/on; earlier Qwen2.5/Gemma/LFM actual generation; Laya actual inference and independent-package typed-output parity.
- Fresh read-only review's important parser findings were fixed and re-reviewed. The added availability guard is intentionally narrow and line-based, not general automatic grounding validation. Manual review remains required.
- Logs: `.local/website-bot-work/qwen3-final-build.log`, `qwen3-final-unit.log`, `qwen3-all-real.log`, and per-run logs. Detailed real-test results: `tests/Harness.Tests/TestResults/qwen3-all-real.trx`.
- Weights, test results and run evidence are Git-ignored. No Kubernetes changes; unrelated pre-existing staged documents are untouched.

ONNX Runtime warns that constant folding of a 1,244,659,712-byte transpose exceeds its default 1 GiB folding limit. This is a runtime optimization warning, not a failed inference; build warnings/errors are reported separately. No unsafe unlimited folding setting is applied.

Historical baseline: [Qwen2.5 validation](qwen-website-summary-validation.md), [Gemma](gemma-website-summary-validation.md), [LFM](website-summary-validation.md).
