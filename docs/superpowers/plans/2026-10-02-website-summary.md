# Local website-summary bot implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for native execution, or superpowers:subagent-driven-development if the user selects delegation. Steps use checkbox syntax for tracking.

**Goal:** Build and validate the requested fully local C# Harness bot against OpenPlateStudio.

**Architecture:** Native ONNX model adapters feed Microsoft's Harness; browser tools expose bounded observations and actions. LFM selects tools and writes the summary; Laya supplies typed relevance decisions. Program.cs only composes these components.

**Tech stack:** .NET 10, Microsoft.Agents.AI.Harness, Microsoft.Extensions.AI, Microsoft.ML.OnnxRuntime, native Hugging Face tokenizer support, Microsoft.Playwright, NUnit.

**Spec:** ../specs/2026-10-02-website-summary-design.md

## Global constraints
- No Python/Node inference service, hosted LLM, GGUF, or substitute model.
- Default URL https://openplatestudio.com/; default headed browser with visible cursor overlay, not OS mouse movement.
- At most 5 pages and 20 browser actions; cancellation and timeouts enforced.
- Model-selected actions become FunctionCallContent; only Harness invokes tools.
- Preserve existing staged changes and Kubernetes configuration; pin packages and model revisions.
- Record genuine tool execution and source text, not just completion claims.

## Review focus
- Tokenizer normalization/special tokens differ between models: compare exact token IDs to fixed reference fixtures.
- LFM cache tensors mix empty attention state and nonempty convolution state: test metadata and multiple generation steps.
- Redirects and subresources can reach private networks: block requests as well as requested navigation URLs.
- Model emits partial reasoning or invalid action JSON: bounded retry, clear failure, no invented tools or summary.
- Long pages exhaust context or classification windows: bounded observations, explicit truncation, summary evidence checks.

## Task 1: Reproducible native model assets and tokenizers
**Files:** modify harness/Harness.csproj; create harness/Models/ModelAssets.cs, harness/Models/NativeTokenizer.cs, scripts/Setup-WebsiteBot.ps1; create tests/Harness.Tests/Harness.Tests.csproj and TokenizerTests.cs.
**Interfaces:** ModelAssets.Load(string directory) returns manifest/config paths and verified identity; NativeTokenizer.Encode(string text) returns int[]; Decode(IReadOnlyList<int> ids) returns string; Dispose releases native state.
- [ ] Add failing NUnit tests for exact LFM and Laya reference IDs, special-token handling, Unicode, incomplete model files, and wrong model identity. Run `dotnet test tests/Harness.Tests` and record the failure before implementation.
- [ ] Select MIT/Apache-compatible native tokenizer package from published API; pin its version and verify runtime has no Python dependency. Pin all other NuGet versions.
- [ ] Implement model asset resolution and tokenizer adapter; setup downloads pinned LFM Q4 graph/data/config/tokenizer and receptron Laya graph/data/config/tokenizer, then writes revision/size/SHA256 manifest. Install Chromium via generated Playwright PowerShell script.
- [ ] Run tokenizer tests and asset verification against real downloaded files; commit only task files, preserving user-staged changes.

## Task 2: Laya typed native decisions
**Files:** create harness/Models/LayaDecisionModel.cs, harness/Models/LayaSequence.cs; tests/Harness.Tests/LayaTests.cs.
**Interfaces:** LayaDecisionModel.Decide(string state, IReadOnlyDictionary<string, DecisionQuestion> questions) returns DecisionResult with per-question typed answers/probabilities. DecisionQuestion carries type, instructions, ordered criteria; support choice, score, noul.
- [ ] Add failing tests for [CLS]/[SEP]/[MASK] sequence, option markers, 512/192 token budgets from config, mask scrubbing, correct false/true ordering, stable softmax, entropy confidence and per-cardinality temperatures.
- [ ] Implement reference sequence building and input_ids/attention_mask/marker_pos/marker_mask/qtype batch tensors in native ONNX Runtime; dispose session/results correctly.
- [ ] Verify typed probability output against fixed upstream reference fixtures and run real relevance decisions; log actual elapsed time without promising a benchmark.
- [ ] Run `dotnet test tests/Harness.Tests --filter Laya` and commit scoped files.

## Task 3: Thinking inference and Harness chat adapter
**Files:** create harness/Models/LfmThinkingModel.cs, harness/Agent/LfmChatClient.cs, harness/Agent/ActionProtocol.cs; tests/Harness.Tests/LfmTests.cs and ActionProtocolTests.cs.
**Interfaces:** LfmThinkingModel.GenerateAsync(IReadOnlyList<ChatMessage> messages, int maxTokens, CancellationToken token) returns generated final text; LfmChatClient implements IChatClient, exposes only observed tool schemas, translates valid action JSON into FunctionCallContent. ActionProtocol.Parse(string output, IReadOnlyList<AITool> tools) returns validated action or final text.
- [ ] Add failing tests for exact chat template, convolution and zero-length KV state initialization, present-to-past mapping, EOS and token limits; action tests reject unknown tools, bad arguments, partial reasoning, and premature final answers without observations.
- [ ] Implement raw ONNX prefill/decode, cache reuse, cancellation, reasoning removal and a bounded structured-action adapter. Final summary is LFM text, never hardcoded.
- [ ] Run real LFM smoke inference and multi-step tool-selection probe. Inspect outputs independently; diagnose failures instead of adding a scripted fallback.
- [ ] Run `dotnet test tests/Harness.Tests --filter "Lfm|ActionProtocol"` and commit scoped files.

## Task 4: Visible bounded browser tools
**Files:** create harness/Browser/BrowserTools.cs, harness/Browser/UrlPolicy.cs, harness/Browser/EvidenceWriter.cs; tests/Harness.Tests/BrowserTests.cs and UrlPolicyTests.cs.
**Interfaces:** BrowserTools.NavigateAsync(string url), ObserveAsync(), ClickAsync(string observedLinkId), ScrollAsync(int pixels), ClassifyAsync(string state) return structured observations/results; all receive cancellation tokens. EvidenceWriter records timestamped tool events, pages and screenshots.
- [ ] Add failing tests for public HTTP(S)/same-origin navigation, localhost/private IPv4/IPv6, redirect and subresource rejection, stale IDs, empty text, limits, cancellation and mouse events using a controlled browser fixture.
- [ ] Implement headed Chromium, cursor overlay, animated Mouse.MoveAsync/ClickAsync, visible-DOM text extraction, observed link IDs and bounded page/action counters. Permit only read-only operations. Private-network fixture access must be an explicit test-only injected policy, unavailable in normal CLI mode.
- [ ] Run browser fixture tests, inspect a headed fixture screenshot and confirm cursor motion events before claiming visibility.
- [ ] Run `dotnet test tests/Harness.Tests --filter "Browser|UrlPolicy"` and commit scoped files.

## Task 5: Compose, validate live, document
**Files:** replace harness/Program.cs; create harness/BotOptions.cs; update README.md; create docs/website-summary-validation.md and artifacts/website-summary evidence.
**Interfaces:** CLI supports --url, --lfm-model, --laya-model, --headless (test mode), --output, --keep-open-seconds and --timeout-seconds; default live URL and headed mode from spec. Main returns nonzero on incomplete or unverified execution.
- [ ] Add failing CLI configuration/exit-code tests and an integration assertion that a real Harness invokes browser and Laya tools before summary acceptance.
- [ ] Compose actual HarnessAgent with LfmChatClient and explicit tools; disable unrelated built-ins. Persist all observations and final output, list observed source URLs and verify the answer against captured content.
- [ ] Run `dotnet build harness/Harness.csproj`, full `dotnet test tests/Harness.Tests`, then `dotnet run --project harness -- --url https://openplatestudio.com/ --output artifacts/website-summary --keep-open-seconds 15` with configured real model directories.
- [ ] Inspect tool logs, model identities, screenshots, page text and final summary. Confirm both models were used, Harness directed actions, and summary describes observed site offerings/audience/features. Record failures and continue debugging until the requested end state is demonstrated.
- [ ] Document exact setup/run/test commands and measured validation results, perform final code review and requirement-by-requirement completion audit; commit only scoped changes.

## Execution choice
Recommend native execution in this session: model formatting, Harness protocol and browser observations are closely coupled; one implementer avoids handoff overhead. Delegation is optional only if selected by the user.

## Execution outcome (2026-10-02)

Tasks 1–5 implemented and validated; see `docs/website-summary-validation.md` for the final 50 regression tests, 3 real-model tests, live Harness execution and grounding review. Scoped adjustments: native LFM tool syntax is supported alongside JSON; a bounded 128-token reasoning section and repetition penalty prevent CPU thinking loops; final evidence is condensed after real classification. All redirects fail closed because Playwright does not reliably intercept subsequent redirect-chain requests. Supply the final public URL directly. WebSockets are blocked. CLI exit-code validation and actual Harness integration were demonstrated by executable/live probes rather than mocked orchestration. The original step checkboxes remain the pre-execution checklist; this outcome and validation report are the authoritative completion record.
