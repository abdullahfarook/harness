# Local website summary bot

**Historical LFM baseline.** The default brain is now Qwen3.5-0.8B Q4 ONNX; use the current [Qwen3.5 setup and timing report](qwen35-website-summary-validation.md). Commands and model paths below describe the earlier LFM version.

## Setup and run

Validated on Windows x64 with .NET SDK 10.0.401. Install .NET 10, PowerShell and curl.exe, then run from the repository root:

```powershell
pwsh -File scripts/Setup-WebsiteBot.ps1
dotnet run --project harness -- --url https://openplatestudio.com/ --output artifacts/website-summary/run-1
dotnet test tests/Harness.Tests --filter "TestCategory!=ModelIntegration"
dotnet test tests/Harness.Tests --filter "FullyQualifiedName~LfmRealInference|FullyQualifiedName~LayaRealInference|FullyQualifiedName~LayaTypedParity"
```

Setup downloads approximately 2.5 GB of model weights and installs Playwright Chromium. No Python or PyTorch inference process is needed. Use a fresh output directory for each run. `--headless` is optional; default is a visible browser with an animated red mouse cursor. `--keep-open-seconds 15` controls the final inspection window. `--timeout-seconds 1800` bounds the whole run. CPU inference can take several minutes.

Override model paths with `--lfm-model` / `--laya-model` or `LFM_MODEL_PATH` / `LAYA_MODEL_PATH`. The application verifies model manifests, required assets, file sizes and SHA256 before inference.

## Models and orchestration

- Microsoft.Agents.AI.Harness 1.23.0 runs the actual tool loop in `Program.cs`.
- LiquidAI/LFM2.5-1.2B-Thinking-ONNX Q4, revision `e7fe61974e3a167dff77c5722db9a1cb7b57140f`: native ONNX Runtime cached decoding and Hugging Face tokenizer. Greedy decoding uses repetition penalty 1.05. At 128 reasoning tokens an unfinished thinking section is explicitly closed through the model cache; the model still generates every tool call and answer.
- Requested convaiinnovations/laya uses the receptron/laya-onnx native export, revision `68f27dfe5a27a54fb2b1fefc432f43f972e90868`. Tests compare choice, score, noul, calibrated probabilities and token count against the independent receptron package fixture.
- Harness exposes readiness-gated navigate/observe/click/scroll/classify tools. Completion requires real page evidence, a successful Laya classification and an observed source URL. If a captured page says COMING SOON, that status must be retained explicitly; a failed check triggers bounded model retries, not a canned-summary fallback.

## Evidence and limitations

Each output directory contains `events.jsonl`, page JSON, screenshots, `models.json`, and, on successful completion, `summary.txt` and `run.json`. `generation-diagnostic.txt` is a diagnostic partial decode, **not** an accepted summary. The run status remains `generated-awaiting-grounding-review`: URL checks do not establish semantic accuracy; compare the summary with captured page text.

Navigation is limited to the target origin, five pages and twenty browser actions. Only GET/HEAD requests are allowed; private address checks are applied. Static public cross-origin resources are permitted. Redirect responses are rejected: supply the final public URL directly. This avoids both incorrect source attribution and Playwright's redirect-chain interception limitation. WebSockets are denied. This is application-level protection, **not a hardened network sandbox**: DNS prechecks cannot prevent all rebinding races. Run only against trusted public targets or add an egress-filtering proxy/network sandbox for hostile sites. Downloads and service workers are disabled. No authenticated sessions or form submission are provided.

## Validation record (2026-10-02)

- Release build: zero warnings/errors.
- Final non-model suite: 50/50 passed, 60.8 seconds (with concurrent live inference). Covers native action parsing, model assets/tokenizers/cache logic, calibrated decisions, readiness gating, availability checks, concurrent evidence writes, public URL policy, redirect/WebSocket/private-subresource rejection, empty pages, cancellation, headed mouse navigation events, stale links and budgets.
- Real Laya inference: passed, 18 seconds; noul 0.9501363630621523 on the reference product state, 49 input tokens.
- Real Laya typed parity: passed, 17 seconds; all three question types and calibrated probabilities match the independent fixture.
- Real LFM inference: passed, 62 seconds; the answer to 6 × 7 contains 42, with the final decoding/reasoning settings.
- CLI invalid-option probe: exit code 1 with a clear diagnostic.
- Live attempts 10 and 11 were not accepted as fully grounded: the first omitted product details; the second described the coming-soon status imprecisely as upcoming updates. The final prompt focuses on captured page text and requires preserving the availability notice.
- **Live attempt 12 passed** with exit code 0: `artifacts/website-summary/live-12/`. Event evidence records actual LFM-generated `navigate(...)` and `classify()` calls, Harness tool execution, the captured public homepage and native Laya relevance probability **0.8679080604370757** (512 input tokens). The screenshot and cursor evidence show 20 mouse-motion events. No scripted tool choice or summary was substituted.
- Manual grounding review: all four final statements are supported by `page-00.json`: modular-monolith offering, engineers/developers audience, AI scaffolding and GraphQL/REST features, and COMING SOON availability. The source URL matches the observed page. The summary is brief, not exhaustive. Documentation/pricing links were placeholders; no claims about working documentation or pricing were made.

Final generated output:

```text
- Product: Modular Monolith architecture
- Audience: Engineers & developers
- Features: AI scaffolding & GraphQL/REST
- Availability: COMING SOON

Source: https://openplatestudio.com/
```

The validation logs are retained locally under `.local/website-bot-work/final-suite-5.log`, `final-laya.log`, `final-lfm.log` and `live-12.log`. Model weights, logs and screenshots are intentionally Git-ignored. A fresh code review identified redirect/WebSocket gaps; both were addressed with regression tests. DNS-rebinding/network-sandbox and semantic-review limitations above remain explicit.
