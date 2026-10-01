# Work ledger

- Architecture approved: separate model, Hermes, Open WebUI; private access.
- User requested continuous execution without further approval pauses. Written-spec/plan gates are therefore superseded by this explicit instruction.
- Ruling: work in the existing unborn repository instead of a worktree; no commits or modifications to user-staged kube.config or plan.md.
- Graphify lookup attempted; repository has no graph.json or existing application source, so targeted upstream searches and local tests supply navigation evidence.
- ARM64 manifests verified for Hermes v2026.9.14, llama.cpp server, Open WebUI v0.9.6 and Python 3.12 Alpine.
- Model revision and published SHA256 verified through official Hugging Face metadata.
- Ruling: bootstrap an administrator via Kubernetes Secret and supported Open WebUI environment variables, with signup disabled, so UI can be fully self-tested rather than deferred for enrollment.
- Metrics API unavailable; capacity preflight uses scheduler requests, not unverifiable utilization claims.
- RED live test: model inference/health and UI login passed, but Hermes rejected truthful context_length=8192 because agent.model_metadata.MINIMUM_CONTEXT_LENGTH is 64000. No completion claimed.
- Ruling: opt-in narrow single-source-file compatibility overlay changes exactly that constant to the actual configured context. The patch checks the pinned upstream line, parses the modified code, preserves the actual 8K window and leaves the application tree otherwise sealed. This is explicitly experimental upstream-unsupported small-context operation; never label it an unmodified official Hermes deployment.
- Managed configuration now deep-merges into persistent config with a backup instead of replacing user-specific unrelated fields.
- GREEN: live model inference, Hermes chat/auth checks, UI authenticated model/chat path, native tool protocol and independently read tool-created file all passed on cluster (revision 3). Full persistence/reinstall audit underway.
- Independent read-only code review found three Important reuse defects; fixed conditional context validation, configurable model alias in smoke tests, and budgeting the Helm test hook's CPU/memory in preflight. Follow-up review requested.
- Static tests, PowerShell native error/quantity tests, and 12 Python merge/checksum/patch/auth/tool-negative tests passed. An actual 2000m model request was rejected by read-only capacity preflight.
- Browser UI automation surfaces are unavailable (empty CUA inventory). Authenticated frontend HTTP flow and local port-forward health are tested; no screenshot/click-through test is claimed.
- Independent review's tunnel-cancellation issue was reproduced by the reviewer; a RED static test exposed blocking WaitForExit, then an interruptible wait fixed it. Real PTY Ctrl+C at port 3001 promptly removed the owned kubectl child (verified process inventory).
- GREEN final protocol suite: 11 checks, including actual llama.cpp props context=8192/slots=1/quantization=Q4_K - Medium, correct arithmetic responses, authenticated UI greeting, and genuine tool-call output. Disk marker independently verified. Restart persistence now running.
- Installer rerun preserved the Kubernetes Secret's UID and credential hash, plus all 3 PVC UIDs. Actual wrong-architecture image and insufficient-CPU preflight were rejected without cluster mutation.
