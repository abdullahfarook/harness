# Qwen3.5-0.8B Q4 local website bot — 2026-10-02

## Setup and validation commands

Windows x64, .NET 10, repository root:

```powershell
pwsh -File scripts/Setup-WebsiteBot.ps1 -Brain qwen35
dotnet build harness -c Release
dotnet run --project harness -c Release -- --thinking off --url https://openplatestudio.com/ --output artifacts/website-summary/qwen35-off-new
dotnet run --project harness -c Release -- --thinking on --url https://openplatestudio.com/ --output artifacts/website-summary/qwen35-on-new
dotnet run --project harness -c Release -- --thinking off --question "What is 6 times 7? Answer briefly." --repeats 3 --output artifacts/website-summary/qwen35-question-new
dotnet test tests/Harness.Tests -c Release --filter "TestCategory!=ModelIntegration"
# Needs all older weights; restore with Setup-WebsiteBot.ps1 -Brain all.
dotnet test tests/Harness.Tests -c Release --filter "FullyQualifiedName~RealInference|FullyQualifiedName~LayaTypedParity"
```

Default thinking is off. Override model directory with `--qwen35-model` / `QWEN35_MODEL_PATH`; use `--seed` (default 42) and optional `--presence-penalty` (0..2). Without an override, the penalty is mode-specific, not zero. Browser is headed with an animated red cursor; use fresh output directories and serial inference. Existing headless, timeout, keep-open, Laya path and evidence-output options remain.

## Exact implementation

- Uses the post-trained [Qwen/Qwen3.5-0.8B](https://huggingface.co/Qwen/Qwen3.5-0.8B) through [its ONNX conversion](https://huggingface.co/onnx-community/Qwen3.5-0.8B-ONNX), revision `c0d619322dad7c4441a8841a53fc59772ddddcc0`. Q4 decoder graph/external data: 876,359 / 485,425,152 bytes; Q4 embedding graph/external data: 857 / 162,897,920 bytes. Identity, revision, required files, sizes and SHA256 are validated.
- Text-only native C# ONNX Runtime 1.30.0 CPU execution on Intel i5-13420H, four inference threads. No vision encoder is loaded; this upgrade does not add image/video input. No remote model service, GGUF substitution or Python/PyTorch inference. Python/onnx was used only to inspect graph metadata, with external weights disabled.
- Hybrid state is not Qwen3's cache: 18 linear layers carry convolution state `[1,6144,4]` and recurrent state `[1,16,128,128]`; six full-attention layers carry key/value state `[1,2,pastLength,256]`. All three output-state families map back into next-token inputs. Text position IDs have shape `[3,1,length]`, with identical positions on all three mRoPE axes.
- Hard thinking-off uses the pinned template's empty thinking block; on uses its open thinking prefix. Returned raw output accounts for the already-prefilled opening tag so strict reasoning parsing can validate the actual generated closing tag. No forced closure or partial-answer acceptance.
- Text-task sampling: temperature 1.0/top-k 20; off top-p 1.0/presence penalty 2.0; on top-p 0.95/penalty 1.5. Seed is initialized once per process; successive generations advance it. Qwen warns this 0.8B model can enter thinking loops; existing bounded output/time limits remain rather than hiding nontermination.
- Native XML-style tool calls use function-name and parameter-name blocks, not Qwen3's JSON convention. Strict parsing checks one action, tool names, parameter names/duplicates/types and required fields before actual Microsoft Harness invocation. Native Laya and visible Playwright remain the tools; no tool call or summary is synthesized.
- Existing readiness, 8192-token input-plus-reserved-output limit, off output cap 1536/on 4096 (questions 512/2048), three validation attempts, default 1800-second timeout, browser read-only/public/same-origin limits and exact source/COMING SOON/architecture-vs-availability guards remain. DNS prechecks are not a hardened network sandbox, and grounding is manually reviewed.

## Diagnosis disclosed

Initial CPU compatibility checks: off arithmetic/tool-call passed; on arithmetic passed but the model refused to classify because the toy tool lacked a description explaining stored text and it assumed missing image/content. Strict parsing rejected that prose. A diagnostic rerun reproduced the refusal. This was not an unsupported CPU operator or repaired tool action.

Added a red-to-green classifier-description regression and clarified that actual Laya automatically retrieves backend-stored **plain website text**, takes no parameters and requires no supplied image. The standalone real-model check now supplies that same truthful contract rather than an unexplained empty schema. Both real native compatibility cases then passed, 1 min 20 s. Raw failed output is preserved at `.local/website-bot-work/qwen35-failed-call-01.txt`; failing logs and `qwen35-compatibility-02.log` remain. Fresh read-only review found no important decoder/protocol/wiring issue.

## Measurements and final verification

| Check | Observed result |
|---|---|
| Simple question, thinking off | 3/3 correct (`42`); startup 3.02 s; answers 1.516 / 0.430 / 0.478 s, mean **0.808 s** |
| Simple question, thinking on | First two correct, native generation 63.30 / 62.00 s (596 / 469 tokens); third hit 2048-token limit in a repeated thinking loop; process exit 1, no unfinished answer accepted. No three-trial mean reported. |
| Headed live off, first attempt | Exit 1 before browser startup: Windows DNS returned “The requested name is valid, but no data of the requested type was found.” No navigation, classification or summary. |
| Headed live on, first attempt | Exit 1 before browser startup: “No such host is known.” No navigation, classification or summary. |
| Headed live off, retry | **Completed and manually grounded**; startup 12.24 s, live 257.13 s, full 269.53 s; summary-only **97.53 s** (65.66 + 31.86 s, one retry). Three decision retries in total. One real Laya call, homepage HTTP 200, headed browser and 20 cursor moves. |
| Headed live on, retry | **Failed** at the existing 1800 s timeout (exit 1). Real navigation and Laya classification succeeded; two summaries (445.15 / 738.52 s) omitted source URLs and were rejected. Third summary attempt continued looping until native ONNX cancellation. No accepted summary or `run.json`. Failed summary phase approximately **1661.02 s**, measured from request event to failure-log timestamp, including retries and cancelled inference. |

Artifacts/logs: `artifacts/website-summary/qwen35-simple-{off,on}-01`, `.local/website-bot-work/qwen35-simple-{off,on}-01.log`, `qwen35-live-{off,on}-01.log`. On-mode successful generation timings are in `events.jsonl`; the rejected looping diagnostic is retained. The off-mode `question.json` has complete per-answer timings. The on-mode question file is intentionally absent because the three-trial run did not finish.

**Initial live attempts were blocked, not passed.** Direct `Resolve-DnsName openplatestudio.com -Type A -Server 1.1.1.1` returned public addresses, while the application's actual Windows `Dns.GetHostAddresses` failed. No OS DNS setting, hosts override, hard-coded IP, safety bypass, cached-page substitute or remote inference fallback was applied. No real summary, summary-only/live timings or screenshot/cursor grounding can be claimed from the two failed `-01` attempts. The Windows resolver subsequently recovered without configuration changes. Both live modes were retried in fresh `qwen35-live-{off,on}-02` directories; the earlier failures remain recorded.

The off retry initially attempted an unsupported fabrication-company answer before reading the website; readiness enforcement rejected it. Invalid classification/prose was also rejected before the model issued a valid native call. Its first summary omitted the source URL and was rejected; the second preserved the exact source, enterprise boilerplate/DDD/GraphQL/REST/AI audience and COMING SOON. Manual comparison against captured text and screenshot accepted the final summary (`grounding-review.json`), noting its terse grouping of background jobs/events. No rejected response became a tool action or final summary.

The on retry produced malformed classification XML on its first two attempts; strict parsing rejected both, then accepted the third real native call. Two summary candidates had closed thinking blocks but lacked observed source URLs; the second also mixed COMING SOON with unsupported current-availability language. Neither was accepted. The third was cancelled by the application timeout while still reasoning. Raw outputs and the last partial diagnostic remain for investigation. `grounding-review.json` records **not generated**, not passed. Thus the implementation and compatibility suite are validated, but successful live summarization with thinking on is **not** validated. Keep the default thinking-off mode for this workload; these measurements do not establish a general performance improvement.

Fresh final verification: Release build exit 0, **0 warnings / 0 errors**; **94/94 regression tests passed** (33 s); **9/9 explicit real-model/parity tests passed** (5 min 51 s), including both Qwen3.5 modes and the older Qwen3/Qwen2.5/Gemma/LFM/Laya compatibility cases. Logs: `.local/website-bot-work/qwen35-final-{build,unit,real}.log`.

Startup includes verification/model loading and browser startup for live runs. Summary-only inference includes prefill, native thinking when enabled, and output decoding; all retries are counted. Download/build and final browser keep-open time are excluded. These are individual observations, not controlled statistical benchmarks or guarantees for arbitrary questions.

Historical comparisons: [Qwen3](qwen3-website-summary-validation.md), [Qwen2.5](qwen-website-summary-validation.md), [Gemma](gemma-website-summary-validation.md), [LFM](website-summary-validation.md). No Kubernetes changes; unrelated staged documents are preserved.
