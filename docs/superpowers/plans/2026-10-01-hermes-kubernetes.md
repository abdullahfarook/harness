# Hermes Kubernetes Implementation Plan

> Execute inline using superpowers:executing-plans. User explicitly requested continuous deployment and self-testing without approval pauses.

**Goal:** Generate reusable Helm/PowerShell files and deploy and test the complete three-component ARM64 stack.
**Architecture:** Open WebUI → authenticated Hermes API → CPU llama.cpp MiniCPM5-2B Q4_K_M, 8192 context, one slot. Separate PVCs and private Services.
**Tech Stack:** Helm 3, Kubernetes, PowerShell 7, Python standard-library HTTP tests.
**Spec:** docs/superpowers/specs/2026-10-01-hermes-kubernetes-design.md

## Global constraints
- Never expose credentials or commit supplied kube.config. Preserve existing cluster workloads and data.
- ARM64 nodes: 1830m allocatable CPU each; model request 1000m, one inference slot, context 8192.
- Images and model revision/checksum immutable; /opt/data and /app/backend/data persistent.
- No public ingress or agent service-account token. Authenticated administrator bootstrap enables unattended UI testing.

## Review focus
- Native command errors must abort scripts, including Helm failures.
- Reruns retain credentials and all PVC data.
- Invalid chart values fail before cluster mutations.
- Bootstrap administrator is random and private; signup disabled.
- Model tool execution must be verified on disk, not accepted from claimed text.

## Task 1: Chart and repeatable deployment
Files: helm/hermes-stack/{Chart.yaml,values.yaml,templates/*,files/*}, scripts/{Common.ps1,Deploy-Hermes.ps1}, tests/Test-Static.ps1.
- [ ] Write static assertions for chart structure, routing, security, context, persistent volumes, and PowerShell parser.
- [ ] Observe missing-chart failure.
- [ ] Implement pinned images, checksum model downloader, independent workloads, existing-secret contract, and fail-fast reusable installer with scoped preflight.
- [ ] Run static tests, negative-value tests, Helm lint and rendered manifest validation.

## Task 2: Protocol, tool and UI tests
Files: helm/hermes-stack/files/smoke.py, templates/tests.yaml, scripts/Test-Hermes.ps1.
Interfaces: Helm test logs emit one sanitized JSON report; Test-Hermes reads it and independently checks the agent's marker on disk.
- [ ] Write failing checks for live protocol and unique tool marker creation.
- [ ] Implement model inference, Hermes authentication/chat/tool call, Web UI signin/discovery/chat, filesystem verification and restart persistence checks.
- [ ] Deploy, observe actual failures, investigate each failing boundary, fix root causes and repeat until mandatory checks pass.

## Task 3: Reuse and completion audit
Files: README.md, .gitignore, docs/deployment-results.json, docs/work-ledger.md.
- [ ] Run installer a second time and verify secret hashes/PVC UIDs unchanged.
- [ ] Run full static/live tests and inspect live imageIDs, parameters and workload status.
- [ ] Document exact reuse/access/test/rollback/uninstall commands, admin credential retrieval without printing secrets, hardware limits and test scope.
- [ ] Review current files and runtime evidence against all original deliverables. Mark complete only after actual deployment and end-to-end tests pass.
