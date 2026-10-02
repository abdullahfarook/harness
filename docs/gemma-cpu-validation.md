# Google Gemma CPU / Hermes validation — 2026-10-02

## Summary

| Exact model / setup | Result |
| --- | --- |
| Google Gemma 3 1B IT QAT Q4_0 | Not inference-tested: official download returned HTTP 401; no local Hugging Face token was available. |
| Google Gemma 4 E2B IT QAT Q4_0, 8192 context | Loaded and passed 10/11 protocol checks; real tool execution failed. |
| Google Gemma 4 E2B IT QAT Q4_0, 65536 context | Passed all 11 protocol checks, independent tool-file verification, and all three restart-persistence checks. A second post-restart tool trial also passed. |

Gemma 4 is validated for these specific smoke-test cases, not certified reliable for arbitrary agent work. The 8K/64K contrast is an observation from a small number of stochastic runs, not proof that context size alone caused the difference.

## Environment and artifacts

Existing ARM64 Kubernetes cluster `k8s-cluster`; existing pinned llama.cpp and Hermes v2026.9.14 images. CPU only, two inference threads, one slot. No GPU, public ingress, new tool permissions, or Hermes source edits were added.

Gemma 4 official artifact:

- Repository: `google/gemma-4-E2B-it-qat-q4_0-gguf`
- Revision: `675cff42a74c774d6cb76f76d8eacb49b48c9b93`
- File: `gemma-4-E2B_q4_0-it.gguf`
- SHA256: `fa401b55b07ee70a54c6dae3903c783a6e65064312529ea57175cb5f8dec6634`
- Download: 3,349,516,256 bytes; checksum verified in the cluster.
- Text-only test: no multimodal projector downloaded.

Gemma 3 official artifact, access checked but not downloaded:

- Repository: `google/gemma-3-1b-it-qat-q4_0-gguf`
- Revision: `d1be121d36172a4b0b964657e2ee859d61138593`
- File: `gemma-3-1b-it-q4_0.gguf`
- Published SHA256: `95e5b8d891cd6a794f66c2a6fb59a41e9562b4660560b854274eceffb628b22a`
- Required next step: accept the Google repository terms and configure a local read-only Hugging Face token. No alternate non-QAT model was substituted.

## Gemma 4 results

### 8K experimental Hermes configuration

Used the existing small-context compatibility patch, 4Gi memory request and 6Gi limit. Model/backend arithmetic, Hermes arithmetic, authentication rejection, model discovery and authenticated Web UI chat passed. Terminal test failed after 63.57 seconds: the model claimed it executed the command, but its API output had no function call and the requested file was absent.

### 64K standard Hermes context requirement

Set context to 65536, disabled the small-context patch, and used 4Gi memory request / 8Gi limit. Runtime independently confirmed `MINIMUM_CONTEXT_LENGTH=64000`, Hermes context 65536, backend context 65536, one slot, and Q4_0 model.

- All 11 protocol checks passed.
- Terminal execution emitted a real `function_call` and `function_call_output`, with exit code 0 and the expected unique token.
- PowerShell independently read the requested file inside Hermes and verified its contents.
- All three PVC restart-persistence checks passed: llama.cpp, Hermes, Web UI.
- A second post-restart terminal trial passed function-call, output and independent disk checks.
- Authenticated streaming arithmetic returned `6 times 7 is 42.`, included the SSE completion marker, and contained no thinking tags.

Observed timings (not a benchmark):

| Check | Seconds |
| --- | ---: |
| Backend arithmetic | 12.84 |
| Hermes arithmetic | 193.15 |
| Authenticated Web UI greeting | 45.62 |
| First real terminal trial | 186.90 |
| Second post-restart terminal trial | 253.83 |
| Post-restart streaming arithmetic | 3.08 |

Process RSS shortly after loading was approximately 3.30 GiB at 8K and 3.70 GiB at 64K. These are individual process readings, not peak pod-memory measurements. CPU-only agent calls remain slow. Two successful tool trials are not a statistical reliability certification.

## Storage and recovery

The approved model PVC expansion from 5Gi to 12Gi remains; volumes cannot shrink in place. Existing models, credentials and user data were preserved. On future Helm deployments, retain `llm.persistence.size: 12Gi` to avoid requesting an invalid shrink.

Test overrides are local: `.local/gemma4-values.yaml` and `.local/gemma4-64k-values.yaml`. Baseline values were captured in `.local/pre-gemma-values.yaml`; `.local/retained-storage-values.yaml` preserves expanded storage when restoring them.

## Evidence

- `artifacts/gemma3-qat-access-results.json`
- `artifacts/gemma4-8k-deployment-results.json`
- `artifacts/gemma4-8k-tool-diagnostics.json`
- `artifacts/gemma4-64k-runtime.json`
- `artifacts/gemma4-64k-deployment-results.json`
- `artifacts/gemma4-64k-protocol-results.json`
- `artifacts/gemma4-64k-tool-diagnostics.json`
- `artifacts/gemma4-64k-additional-results.json`

Final live-state verification: restored LFM2.5-1.2B-Thinking at 8192 context; Hermes health and all three main workloads Ready. Evidence: artifacts/post-gemma-restoration.json.
