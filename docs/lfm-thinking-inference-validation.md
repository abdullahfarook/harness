# LFM2.5-1.2B-Thinking compatibility test — 2026-10-02

Redeployed the existing ARM64 Helm stack on `k8s-cluster` with the official LiquidAI Q4_K_M GGUF. Existing credentials and all three PVCs were preserved. LFM remains deployed; there was no existing Helm release to roll back to.

## Configuration

- Repo: `LiquidAI/LFM2.5-1.2B-Thinking-GGUF`
- Revision: `9584426effbfa44b677e925793a12188a36b44ce`
- File: `LFM2.5-1.2B-Thinking-Q4_K_M.gguf`
- SHA256: `7223a2202405b02e8e1e6c5baa543c43dc98c1d9741a5c2a0ee1583212e1231b`
- Existing pinned llama.cpp/Hermes images; CPU only, two inference threads, 8192 context, one slot.
- Local override: `.local/lfm-thinking-values.yaml`; chart defaults remain Hermes-3. Pass this override on future deployments to retain LFM.

## Result: not validated for agent tools

10/11 live protocol checks passed, including model inference, correct Hermes arithmetic chat, authentication rejection, and authenticated Web UI chat. The real terminal-tool test failed after 185.54 seconds: the model answered `Command completed.` but emitted only a message, no function call or function output. An independent filesystem check confirmed the requested marker file does not exist.

The arithmetic Hermes test took 155 seconds; Web UI greeting took 41.24 seconds. These are individual observations, not a reliability or performance benchmark.

A separate backend probe returned the correct answer to 6 times 7 in 13.6 seconds, but embedded `<think>` tags in message content rather than returning a separate reasoning field. Hermes separated reasoning from the final answer in the stored tool-test conversation; its answer had no thinking tags. This does not establish correct handling for every response or streaming mode.

The standard runner stopped before restart-persistence checks. Separate follow-up tests subsequently verified restart persistence for all three workloads. All three main workloads were Ready after testing.

## Follow-up validation

- Direct backend function-calling probes with a short system prompt returned valid `get_weather(city="Karachi")` calls for both automatic and required tool choice. These probes did not execute a weather service and are not end-to-end Hermes tool tests.
- Temporarily changed the backend to LiquidAI's recommended sampling settings: temperature 0.05, top-k 50, repetition penalty 1.05. The same real Hermes terminal test still failed after 264.97 seconds: only a message (`Done.`), no function call/output, and no marker file. Sampling alone did not resolve the failure.
- Called Hermes's actual terminal-tool implementation directly: requirements were met, execution returned exit code 0 and the expected token, and an independent filesystem check passed. This validates the terminal infrastructure, not model-driven execution.
- Restored and verified the original inference arguments. No diagnostic sampling changes remain in the deployment.
- Restarted Hermes, Web UI and llama.cpp individually; verified a unique marker survived on each persistent volume, then removed the markers. All three checks passed. Model startup also reverified the cached GGUF checksum.
- After restarts, authenticated streaming chat returned `42` for 6 times 7, included the final SSE completion marker, and contained no thinking tags. Duration: 119.13 seconds. Together with the earlier non-streaming check, this validates those individual requests, not all possible reasoning outputs.

**Final verdict:** this exact model/build/8K configuration is operational for the tested chat paths, but fails model-driven terminal execution. The evidence does not establish whether prompt complexity, model capability, or another agent integration detail is responsible; there is no validated agent-tool fix. Do not use the model's completion claims as evidence that commands ran. LFM remains deployed with the original chart settings.

Recommended sampling reference: [LiquidAI model card](https://huggingface.co/LiquidAI/LFM2.5-1.2B-Thinking).

## Evidence

- `artifacts/lfm-thinking-deployment-results.json`
- `artifacts/lfm-thinking-tool-summary.json`
- `artifacts/lfm-thinking-diagnostics.txt`
- `artifacts/lfm-thinking-direct-tool-probe.jsonl`
- `artifacts/lfm-thinking-recommended-sampling-tool.json`
- `artifacts/lfm-thinking-terminal-infrastructure.txt`
- `artifacts/lfm-thinking-persistence-results.json`
- `artifacts/lfm-thinking-streaming-results.json`

Repeat deployment and tests with:

```powershell
pwsh -NoProfile -File ./scripts/Deploy-Hermes.ps1 -ValuesFile .local/lfm-thinking-values.yaml
```

The 8K Hermes compatibility patch remains experimental, as documented in README.md. Chat success alone is not evidence of usable agent-tool execution.
