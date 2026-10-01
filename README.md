# Hermes + Hermes-3-3B + Open WebUI on ARM64 Kubernetes

Three separate workloads: **Open WebUI → Hermes Agent → llama.cpp / Hermes-3-Llama-3.2-3B Q4_K_M**. Model context is **8192**, parallel slots **1**. Each component has its own persistent volume. Access is private by default.

## Deploy / upgrade / test

Requirements: PowerShell **7+**, `kubectl`, Helm **3+**, Docker CLI with Buildx (registry manifest inspection only; no local daemon/build required), an authenticated kubeconfig, a Ready ARM64 cluster and a working StorageClass. This cluster uses `longhorn`; changing StorageClass after PVC creation requires an explicit data migration. The images are pinned to ARM64 digests and checked during preflight.

Run from this repository:

```powershell
# Read-only preflight: tools, chart, architecture, scheduler requests and storage
pwsh -NoProfile -File .\scripts\Deploy-Hermes.ps1 -ValidateOnly

# Deploy or rerun; by default also test HTTP protocols, real tools and restart persistence
pwsh -NoProfile -File .\scripts\Deploy-Hermes.ps1

# Independently repeat the complete live tests
pwsh -NoProfile -File .\scripts\Test-Hermes.ps1

# Local regression checks, and Python helper tests inside the deployed Hermes container
pwsh -NoProfile -File .\tests\Test-Static.ps1
pwsh -NoProfile -File .\tests\Test-Scripts.ps1
pwsh -NoProfile -File .\tests\Test-Python.ps1
```

Defaults: `kube.config` in the repo root, its current context, namespace/release `hermes`, timeout `30m`. Every cluster/Helm command explicitly targets that kubeconfig and context. The installer throws on nonzero command exits and preserves existing credentials on reruns. The Kubernetes Secret is separate from Helm values/history; secrets are never printed.

Reuse with another compatible ARM64 cluster:

```powershell
pwsh -NoProfile -File .\scripts\Deploy-Hermes.ps1 `
  -KubeConfig C:\private\other.kubeconfig -Context my-cluster `
  -Namespace hermes -Release hermes -ValuesFile .\my-values.yaml -Timeout 45m
```

Override files are normal Helm values; do **not** put secrets in them. `-SkipTests` and `-SkipPersistenceTests` are explicit operator opt-outs, not the default deployment path. `Test-Hermes.ps1 -SkipPersistence` runs the protocol/tool checks without restarts. Reports are sanitized JSON at `artifacts/deployment-results.json`, or use `-ReportPath` to choose another path.

## Open the Web UI

```powershell
pwsh -NoProfile -File .\scripts\Open-Hermes.ps1 -CopyAdminPassword
```

This creates a localhost-only tunnel, opens `http://127.0.0.1:3000`, prints the administrator email, and copies its bootstrap password to your clipboard without printing it. Paste the password into the sign-in form. Keep the script running; Ctrl+C stops its own tunnel. Clear the clipboard after signing in. Use `-Port 3001` if 3000 is busy. The initial administrator email is `hermes-admin@localhost.local`; the random password and session signing key live in the Kubernetes Secret. Signup is disabled, and account/chat data persists in the UI PVC. If you change the admin password through the UI, update the Secret used by unattended tests too.

Do not expose the Services publicly without separately designing TLS, authentication, authorization and network controls. The agent can execute commands inside its container; only trusted administrators should use it. It does not receive Kubernetes API credentials, host mounts or a Docker socket. ClusterIP is not a network-isolation guarantee: other cluster workloads may reach the model Service. A CNI-enforced NetworkPolicy would be needed for stronger tenant isolation.

## Important: Hermes 8K compatibility

The pinned official `v2026.9.14` image enforces a **64,000-token minimum**, so the original plan's unmodified Hermes + 8K setup does **not** work. This chart deliberately opts into an **experimental compatibility patch**:

- An init container checks that upstream has exactly `MINIMUM_CONTEXT_LENGTH = 64_000`.
- It changes only that constant to the configured actual context (8192), checks Python syntax, and mounts only that patched source file read-only from an ephemeral volume.
- The model and Hermes still truthfully use **8192**; no fake 64K metadata or larger model is substituted.
- The rest of `/opt/hermes` remains the official installed application, never covered by a PVC.
- Tools are kept small (terminal), runs have an 8-turn / 600-second budget, and repeated failing tool loops hard-stop. Built-in persistent memory/state is retained, but the full large-model toolset is not enabled.

This is **not unmodified upstream-supported Hermes operation**. The small model/context limits long tasks and reasoning quality. The patch fails closed if the pinned source guard changes. To use unmodified Hermes, set `hermes.allowSmallContext: false` **and** increase model context to at least 64000, rechecking memory/CPU capacity; that is a different deployment from the requested 8K stack.

Managed Helm configuration deep-merges into `/opt/data/config.yaml` at startup with a `.pre-helm` backup; unrelated configuration, sessions, memories, skills and `.env` survive. Helm owns the managed keys, so change those through values, not direct edits. Model downloads use an immutable Hugging Face revision and the published SHA256. A valid cache is reused on restart.

## Test coverage and limitations

Helm test verifies model discovery/inference, Hermes health/auth rejection/discovery/chat, real tool-call output, and UI health/signin/model discovery/authenticated chat. The PowerShell runner **independently reads the tool-created file in Hermes** and tests restart persistence on all three PVCs. Fake text saying “done” cannot pass. The report includes actual runtime imageIDs and doctor output.

`hermes doctor` can return exit 0 while reporting optional missing providers, an image-specific CLI symlink warning, and advisories in unused browser/build tooling. Those diagnostics are recorded rather than misrepresented as a clean security audit. External search/image/messaging services are not configured. No GPU, public ingress, Laya or 200-call statistical reliability certification is claimed. Browser automation availability is separate from the authenticated UI HTTP test.

Initial requests are model 1000m CPU / 2Gi, Hermes 100m / 256Mi, UI 100m / 512Mi; limits are in `helm/hermes-stack/values.yaml`. Each node here has 1830m schedulable CPU, so the original 2-core **request** would never schedule. Metrics Server is not installed; preflight uses actual scheduler requests, not measured CPU utilization. Initial images/model download can take several minutes. CPU-only inference is intentionally modest.

## Status / recovery / uninstall

```powershell
kubectl --kubeconfig .\kube.config --context k8s-cluster -n hermes get pods,pvc,services
helm --kubeconfig .\kube.config --kube-context k8s-cluster -n hermes history hermes
# Replace N with a previously working revision; then rerun tests.
helm --kubeconfig .\kube.config --kube-context k8s-cluster -n hermes rollback hermes N --wait --timeout 30m
# Removes workloads/services/config, but leaves PVCs and bootstrap Secret.
helm --kubeconfig .\kube.config --kube-context k8s-cluster -n hermes uninstall hermes
```

PVCs carry Helm's `resource-policy: keep`. Back up all three volumes before upgrades or uninstall. Uninstall deliberately does **not** delete persistent data or the credential Secret. Do not delete the namespace/PVCs unless you explicitly intend to destroy that data. The installer does not roll back automatically on failure: evidence and data are left available for diagnosis.

The supplied `kube.config` was already staged by you. `.gitignore` prevents new accidental additions but cannot untrack an already-staged file: remove it from the index before your own first commit if you intend to keep credentials private. This task does not change your staging or make commits.

## Upstream references

- [Official Hermes/Open WebUI integration](https://github.com/NousResearch/hermes-agent/blob/main/website/docs/user-guide/messaging/open-webui.md)
- [Official Hermes Docker persistence and entrypoint](https://github.com/NousResearch/hermes-agent/blob/main/website/docs/user-guide/docker.md)
- [Official MiniCPM llama.cpp deployment](https://github.com/OpenBMB/MiniCPM/blob/main/docs/deployment/llama_cpp.md)
- [Pinned GGUF repository](https://huggingface.co/NousResearch/Hermes-3-Llama-3.2-3B-GGUF/tree/3cd927095d8cbab12c743f932aa63b6f7bbfa141)
