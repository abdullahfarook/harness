# Hermes Kubernetes deployment design

## Intent and approved architecture
Deploy MiniCPM5-2B Q4_K_M with an 8192-token context, Hermes Agent, and Open WebUI as three separate Kubernetes workloads. Supply a reusable Helm chart and PowerShell deployment/test scripts. Use the supplied kube.config without copying credentials into generated files. The user approved Open WebUI and private initial access.

## Components and interfaces
- llama.cpp Deployment: CPU ARM64 image, one replica, one inference slot, conservative batching, OpenAI-compatible Service on port 8080. Download the official openbmb/MiniCPM5-2B-GGUF Q4_K_M artifact into a persistent model cache; validate the artifact against its published checksum when available. Hermes uses the internal /v1 endpoint.
- Hermes StatefulSet: official ARM64 image, one replica, original entrypoint with gateway run. Persist /opt/data, never mount over /opt/hermes. Enable its authenticated API server on port 8642. Configure custom model routing, bounded iteration/tool-loop guardrails, and local terminal tools. No host mounts, Docker socket, or Kubernetes service-account token. Agent commands run inside its own container, not on the node.
- Open WebUI Deployment: ARM64 image, one replica, Service on port 8080. Connect only to Hermes /v1 with a shared API key, disable Ollama. Preserve account/chat data at /app/backend/data. Authentication remains enabled. First administrator enrollment is performed through the private UI; disable further registration after enrollment.
- All Services are ClusterIP. Access the UI with a localhost-only kubectl port-forward. Public ingress, TLS/domain configuration, Laya, external search credentials, and GPU support are outside the initial scope.

## Cluster-specific constraints
Read-only preflight found two Ready ARM64 nodes, each reporting 1830m allocatable CPU and approximately 9.2 GiB allocatable memory. Initial model CPU request must be below 1830m (start at 1000m); compute free schedulable resources before installation. Start model memory request at 2 GiB and limit at 4 GiB, then adjust only with measured evidence. Hermes and UI use small CPU requests and bounded memory limits.

Explicitly select a discovered StorageClass instead of relying on the cluster's two default classes. Validate provisioning, access mode, and scheduling behavior before choosing the deployment override. Model cache, Hermes state, and UI state each get a separate PVC. Keep persistent data on upgrade and uninstall unless the operator explicitly deletes it.

## Reusable files and installer behavior
- helm/hermes-stack/: Chart.yaml, values.yaml, workload/config/service/PVC templates, and Helm test hook.
- scripts/Deploy-Hermes.ps1: parameters for kubeconfig, context, namespace, release, values overrides, timeout, and validation-only mode. Use PowerShell 7, explicit kubeconfig/context on every Kubernetes and Helm command, and fail on nonzero native exit codes. Validate tools, cluster, architecture, capacity, storage, and image compatibility; lint/render the chart; install/upgrade; wait; run tests. Preserve existing secrets on rerun. Never print credential values or put them into command-line Helm overrides or committed values.
- scripts/Test-Hermes.ps1 and tests/: static/script tests and live smoke tests. Emit a machine-readable timestamped test report with pass/fail and sanitized diagnostics. Stop owned port-forward processes in finally blocks.
- README.md: prerequisites, exact install/upgrade/test/access commands, image and model pins, limitations, rollback and uninstall/data-retention instructions.
- .gitignore: ignore kubeconfig, local secrets, generated private overrides, and transient logs. Do not modify or commit the already-staged kube.config.

## Versions and configuration
Verify ARM64 manifests and upstream CLI/config contracts before selecting image versions. Resolve and record immutable image digests after successful tests; do not assume the example tag in plan.md exists or works. Keep all model/image/resource/storage settings configurable in Helm values. An upgrade must reconcile managed configuration without deleting user sessions, memories, skills, or UI accounts.

## Verification and success criteria
1. Static tests: chart lint/render, separate workloads and PVCs, 8192 context/one slot, correct routing, authentication, no service-account token or public exposure, and PowerShell syntax/exit-code behavior. Negative cases include invalid values, missing tools, unreachable cluster, wrong architecture, insufficient capacity, and failed native commands.
2. Live tests: PVCs bound, all workloads ready, actual Hermes version/doctor output checked, llama.cpp model discovery and nonempty inference, Hermes authentication rejects missing/invalid keys, and Hermes model discovery and nonempty chat response.
3. Tool execution: ask Hermes to create a uniquely named harmless marker under /opt/data and verify that file through kubectl exec, rather than accepting claimed execution in model text. Delete only test-owned marker files after verification.
4. UI: health and sign-in page load; verify its configured upstream reaches Hermes. After administrator enrollment, test an authenticated chat through the UI where feasible; report any enrollment-dependent check as not performed rather than passed.
5. Persistence: write test-owned markers, restart workloads individually, check markers survive, and verify an installer rerun retains secrets and state. Run Helm test and record its outcome.

Deployment is complete only when mandatory checks pass on this cluster. Report any model parser/tool failure, dependency issue, or unperformed UI test explicitly. A smoke suite is not the 200-call statistical reliability benchmark described in plan.md; that benchmark is a separate optional follow-up and must not be claimed as executed.

## Risks and recovery
Small-model tool calling and the MiniCPM chat template/parser need empirical testing. Failure to execute the tool marker blocks declaring the agent fully functional. Downloads and image pulls may require longer startup timeouts. Capture sanitized pod events/logs on failure, leave persistent data intact, and give rollback instructions; never delete unrelated namespace resources. The local OCI auth plugin emits PowerShell module-loading warnings even though read-only cluster commands currently succeed; preflight must distinguish warnings from actual authentication failure.

## Alternatives considered
Recommended: one Helm release managing three separate workloads, simplest rerun and unified tests. Separate releases allow independent lifecycles but complicate secret/routing coordination. A bundled Hermes dashboard is simpler but does not satisfy the agreed separate frontend architecture.

## Review status
Architecture approved in chat. This written spec awaits review before implementation planning and deployment.
