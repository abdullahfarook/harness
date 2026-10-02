# Local website-summary bot design

## Outcome and scope
Replace the Qwen/Nomic demo entry point in `harness/Program.cs` with a .NET 10 console bot using Microsoft's actual `Microsoft.Agents.AI.Harness` package. Default target: `https://openplatestudio.com/`; allow `--url` overrides. The bot must visibly navigate, collect real page text, and print a source-grounded summary. Both model inference paths execute locally in C# using ONNX Runtime; no Python, Node inference service, hosted LLM, or substitute model.

## Architecture
`Program.cs` composes configuration, model sessions, headed Playwright browser tools, and a HarnessAgent. Keep implementation in focused classes rather than adding another large top-level script.

- **Thinking brain:** LiquidAI/LFM2.5-1.2B-Thinking-ONNX Q4 graph and external weights. Implement its tokenizer, chat template, prefill, token generation, and convolution/attention cache handling. Expose generation through `IChatClient`. Remove reasoning blocks from user-visible output. Do not assume compatibility with the old Qwen ONNX GenAI configuration.
- **Quick brain:** the ONNX export of convaiinnovations/laya distributed as receptron/laya-onnx. Implement native tokenization, reference sequence layout, option marker tensors, temperature calibration, and typed choice/score/noul results. Use short page/link state for relevance decisions; uncertain classifications are advisory, not an excuse to skip all content.
- **Harness:** controls tool invocation and history. The thinking model chooses tools using a small explicit structured action protocol translated into `FunctionCallContent` by the chat adapter. Harness, not a separate hidden orchestration loop, invokes the tools. Reject unknown tools and malformed arguments; bounded recovery may request corrected actions, never invent an action on the model's behalf. Disable unrelated built-in capabilities to keep prompts tractable for a 1.2B model.
- **Browser tools:** navigate, observe visible page text and eligible links, move/click an observed link, scroll, and obtain Laya decisions. Launch Chromium headed by default. Animate a visible in-page cursor overlay using Playwright mouse events. This is not Windows OS pointer movement. Keep the browser open briefly/configurably after completion for inspection; support a headless test mode without pretending that it proves visible operation.

## Data flow
User URL -> Harness prompt -> LFM action -> Harness browser tool -> real observation -> optional Laya relevance tool -> LFM action -> additional observations -> LFM final summary -> console and evidence artifacts. Record tool events, visited URLs, page text, classification probabilities, screenshots, and final answer. Default budget: at most 5 pages and 20 browser actions, with cancellation and per-navigation timeouts. Extract bounded text from actual visible DOM and maintain a token budget before generation.

## Boundaries and failure behavior
Only HTTP(S) public targets; block localhost/private-IP destinations, unsafe schemes, cross-origin navigation, and redirects outside the allowed origin. Validate each URL before navigation and browser requests; allow necessary public cross-origin static resources but never private-network requests. Read-only browsing: no form submission, purchases, authentication, downloads, or arbitrary JavaScript supplied by the model. Website text is untrusted data, never instructions. Use IDs from observed links rather than model-generated selectors.

Fail clearly with nonzero exit code for missing/incomplete weights, incompatible graph/tokenizer metadata, exhausted generation without a final answer, invalid action after bounded retries, navigation failure, empty evidence, or an ungrounded completion. Never claim a website summary based on demo documents or a fallback canned answer. Validate output and tool execution independently of model completion claims.

## Configuration and reproducibility
Use explicit model-directory options/environment variables; provide a PowerShell setup script to download the exact model graphs, external data, tokenizer/config files and install Playwright Chromium. Record model revision, file sizes and checksums in a manifest. Pin NuGet versions and model revisions during implementation. Preserve unrelated user-staged files and Kubernetes deployment configuration. Model downloads require several GB disk/RAM; startup/download time is not an inference benchmark.

## Acceptance evidence
1. `dotnet build` succeeds with pinned dependencies and .NET 10; `Program.cs` constructs a real HarnessAgent.
2. Automated tests cover Laya sequence/marker layout, mask injection scrubbing, calibration/softmax/noul ordering; verify tokenizer IDs and real-model probabilities against the reference implementation on fixed inputs.
3. LFM smoke inference runs the specified ONNX graph, with correct cache shapes and end-token handling; tests cover action translation, malformed actions, reasoning removal, and cancellation.
4. Local browser fixture tests cover DOM extraction, observed-link clicks, cursor animation, page/action limits, and unsafe URL/request rejection, including redirect destinations.
5. A real headed run visits OpenPlateStudio with Harness-invoked browser actions and both models actually used. Capture action log, screenshots, extracted pages, model manifest, and generated summary under `artifacts/website-summary/`.
6. Inspect the generated summary against captured source text: identify what the site offers, audience, key capabilities, and observed source URLs. Report partial/inaccessible pages honestly. A successful HTTP fetch or unit test alone does not satisfy end-to-end validation.
7. Document setup/run/test commands, visible-cursor limitation, and observed runtime results without claiming unmeasured speed or general tool reliability.

## Alternatives considered
- Python/Node model service: easier reference reuse, rejected to keep inference in C# as agreed.
- HTTP-only scraper: simpler, rejected because visible browser navigation directed by Harness is required.
- ONNX GenAI replacement model: rejected because the requested LFM export must run, rather than silently substituting Qwen or a GGUF service.

## Sources and current findings
- Microsoft Harness: https://learn.microsoft.com/en-us/agent-framework/concepts/harness?pivots=programming-language-csharp
- LFM graph/cache reference: https://huggingface.co/LiquidAI/LFM2.5-1.2B-Thinking-ONNX
- Laya native ONNX reference: https://github.com/receptron/laya (`src/laya.ts`, `src/sequence.ts`).
- Read-only checks on 2026-10-02: .NET SDK 10.0.401 available; Harness NuGet versions through 1.23.0 published; target site returned HTTP 200 with title `OpenPlate | Enterprise Application Boilerplate`. These checks do not validate the bot or summary.
