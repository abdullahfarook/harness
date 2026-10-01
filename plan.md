Hermes ARM64 / Kubernetes deployment report
The research did uncover why the ARM64 stage was taking too long: Hermes now has an official ARM64 Docker image, so we do not need to invent a replacement image. The current Docker registry lists both linux/amd64 and linux/arm64, and Hermes' own platform documentation classifies Docker on both architectures as Tier 1. Docker Hub
The practical architecture I would use is:
                    Kubernetes
                        │
             ┌──────────▼──────────┐
             │    Hermes Agent     │
             │ gateway / agent     │
             └───────┬─────────────┘
                     │ OpenAI-compatible API
                     ▼
             ┌─────────────────────┐
             │ Local LLM Server    │
             │ llama.cpp / SGLang  │
             │ MiniCPM5 / LFM /    │
             │ Qwen3.5             │
             └─────────────────────┘

Optional:
 Hermes ──MCP──► Laya 421M decision/router
 Hermes ───────► native persistent memory

For genuinely constrained ARM64 hardware, I would not start with Qwen3.5-9B. Start with MiniCPM5-2B Q4_K_M, with LFM2.5-2.6B Q4_K_M as the second model to test. Both have official GGUF distributions and documented llama.cpp operation. Hugging Face
1. ARM64 status: resolved, with one caveat
Current Hermes Agent Docker images show a native ARM64 manifest. The release tooling also explicitly defines amd64 and arm64, and the current CI has a native ARM64 build job. GitHub
So Kubernetes should simply use:
image:
  repository: nousresearch/hermes-agent
  tag: v2026.9.14

I recommend a versioned tag rather than latest.
There is, however, an important historical ARM64 warning. A July 2026 bug report found that an ARM64 image accidentally contained x86-64 Python wheels. The report confirmed that rebuilding the unmodified Dockerfile natively on ARM64 produced a correct ARM64 environment; a proposed fix explicitly changed dependency resolution to select ARM64 wheels. The issue remains visible/open in the tracker, so I would make an actual Hermes command—not merely Docker's health state—part of your deployment smoke test. GitHub
In other words:
ARM64 manifest exists        YES
Official ARM64 support       YES
Current CI builds ARM64      YES
Blindly trust "healthy"      NO
Run hermes version/doctor    YES
Pin tested image digest      YES

Hermes ARM64 Docker bug tracker
2. Lowest-resource model configuration
Tier A — my starting point: MiniCPM5-2B
The official MiniCPM5-2B GGUF repository provides direct llama.cpp support, including Q4_K_M.
Run something conceptually like:
llama-server \
  -hf openbmb/MiniCPM5-2B-GGUF:Q4_K_M \
  --host 0.0.0.0 \
  --port 8080 \
  -c 8192

For small ARM systems, do not start at a gigantic context size. Context/KV cache can turn a tiny model into a surprisingly memory-hungry service.
I would start around:
Model             MiniCPM5-2B
Quantization      Q4_K_M
Context           8K
Parallel slots    1
Batch             conservative
LLM replicas      1
Hermes replicas   1
Laya              optional

Once stable, test 16K.
Tier B — alternative: LFM2.5-2.6B
LFM2.5-2.6B GGUF also officially documents llama.cpp and Q4_K_M operation. It remains one of the more interesting alternatives for low-power CPUs. Hugging Face
I would benchmark it against MiniCPM rather than assume one wins on your ARM CPU:
MiniCPM5-2B Q4_K_M
        versus
LFM2.5-2.6B Q4_K_M

Measure:
tokens/sec
peak RSS
time-to-first-token
valid tool-call %
executed tool-call %
multi-step task completion %
malformed arguments %
tool-loop rate

The executed tool-call rate is more important than whether the generated text merely resembles a function call.
3. Hermes configuration
Hermes' current configuration supports explicit tool-use enforcement. Its documented default "auto" does not automatically enable this for Qwen, so for a local small-model deployment I would explicitly enable it. Hermes also has execution guidance intended to improve persistence and verification. GitHub
Start with:
model:
  provider: custom
  base_url: http://llm:8080/v1
  default: local-model
  api_mode: chat_completions

agent:
  tool_use_enforcement: true
  execution_guidance: true

tool_loop_guardrails:
  warnings_enabled: true
  hard_stop_enabled: true

  warn_after:
    exact_failure: 2
    same_tool_failure: 3
    idempotent_no_progress: 2

  hard_stop_after:
    exact_failure: 5
    same_tool_failure: 8
    idempotent_no_progress: 5

Hard-stop guardrails are particularly appropriate for Kubernetes because Hermes documentation specifically recommends them for unattended gateway/server deployments. GitHub
This prevents a weak 2B model from consuming CPU indefinitely doing:
tool → error
tool → same error
tool → same error
tool → same error
...

4. Laya: useful, but don't make it the brain
The earlier design remains sound:
Hermes
   │
   ├── Main LLM ──► planning / generation / tool arguments
   │
   ├── Laya ──────► cheap classification / routing
   │
   └── Memory ────► Hermes persistent state

The current Laya repository provides an MCP server:
pip install "laya[mcp]"
laya-mcp-server

and supports CPU operation with settings such as:
LAYA_DEVICE=cpu
LAYA_PRELOAD=1
LAYA_MODELS=english,multilingual
LAYA_THREADS=<physical cores allocated>

Its MCP interface returns structured JSON and includes routing/decision/shortlisting operations. The project explicitly positions Laya for structured decisions rather than open-ended Q&A or text generation. GitHub
That makes Laya appropriate for questions like:
Does this request require web access?     yes/no
Should this fact be stored in memory?     yes/no
Which tool family should handle this?     enum
Are these search results relevant?        score
Does the image model need to run?         yes/no
Should Hermes continue another step?      yes/no

Not:
Write the final answer.
Write Python.
Generate tool arguments.
Plan the whole task.

Those stay with the main LLM.
5. Kubernetes design
I would deliberately keep this much simpler than a production cloud microservice architecture.
namespace: hermes

┌────────────────────────────────────────────┐
│ StatefulSet hermes                         │
│                                            │
│ official Hermes ARM64 image                │
│ PVC /opt/data                              │
└─────────────────┬──────────────────────────┘
                  │
                  │ HTTP
                  ▼
┌────────────────────────────────────────────┐
│ Deployment llm                             │
│                                            │
│ llama.cpp server                           │
│ MiniCPM5-2B Q4_K_M                         │
│ PVC/model cache                            │
└────────────────────────────────────────────┘

optional

┌────────────────────────────────────────────┐
│ Deployment laya                            │
│ ~421M decision model                       │
└────────────────────────────────────────────┘

Hermes' Docker documentation identifies /opt/data as the persistent source of truth containing configuration, secrets, sessions, memories, skills, cron definitions and logs. Therefore that—not /opt/hermes—is what should get your Kubernetes PVC. GitHub
Do not mount a PVC over /opt/hermes.
Use:
volumeMounts:
  - name: hermes-data
    mountPath: /opt/data

not:
mountPath: /opt/hermes   # DON'T

The latter contains the installed application tree in the published image. GitHub
6. Resource starting points
For a small ARM64 Kubernetes node, I would begin approximately here rather than massively over-allocating:
Component	Request	Limit
Hermes	256 Mi / 250m CPU	1–1.5 Gi / 2 CPU
MiniCPM5 Q4	2 Gi / 2 CPU	4 Gi / 4 CPU
Laya	512 Mi / 250m	1.5 Gi / 2 CPU


These are deployment starting values, not guaranteed requirements. Actual memory depends heavily on llama.cpp build, context, KV-cache type, batch settings and workload.
For a machine with only ~4 GB total RAM, I would skip Kubernetes entirely unless Kubernetes itself is a requirement. Container/runtime/control-plane overhead competes directly with inference.
For roughly 8 GB RAM:
Hermes
MiniCPM5-2B Q4
8K context
Laya optional
single inference slot

is realistic enough to investigate.
At roughly 16 GB:
MiniCPM5-2B / LFM2.5-2.6B
        ↓
Qwen3.5-4B
        ↓
Qwen3.5-9B Q4

become progressively more reasonable depending on CPU/GPU acceleration.
7. ARM64 preflight should be mandatory
Before Helm considers the installation good, test the actual binary.
For example:
docker run --rm \
  --platform linux/arm64 \
  nousresearch/hermes-agent:v2026.9.14 \
  version

And inspect the manifest:
docker buildx imagetools inspect \
  nousresearch/hermes-agent:v2026.9.14

Then Kubernetes:
kubectl exec -n hermes statefulset/hermes -- \
  hermes version

and ideally:
kubectl exec -n hermes statefulset/hermes -- \
  hermes doctor

This matters because the historical ARM64 packaging failure could leave the container superficially healthy while an actual Hermes invocation failed. GitHub
After the image passes, pin its digest for the stable deployment rather than continuously consuming a moving latest.
8. Helm deployment strategy
I would structure the chart as:
hermes-stack/
├── Chart.yaml
├── values.yaml
└── templates/
    ├── namespace.yaml
    ├── hermes-statefulset.yaml
    ├── hermes-service.yaml
    ├── hermes-pvc.yaml
    ├── configmap.yaml
    ├── secret.yaml
    ├── llm-deployment.yaml
    ├── llm-service.yaml
    ├── model-pvc.yaml
    ├── laya-deployment.yaml
    └── networkpolicy.yaml

with values approximately:
architecture: arm64

hermes:
  image:
    repository: nousresearch/hermes-agent
    tag: v2026.9.14

  persistence:
    enabled: true
    size: 5Gi

  resources:
    requests:
      cpu: 250m
      memory: 256Mi
    limits:
      cpu: "2"
      memory: 1536Mi

llm:
  enabled: true

  model:
    repo: openbmb/MiniCPM5-2B-GGUF
    quant: Q4_K_M

  context: 8192

  resources:
    requests:
      cpu: "2"
      memory: 2Gi
    limits:
      cpu: "4"
      memory: 4Gi

laya:
  enabled: false

I would leave Laya off on the first boot.
Get this stable first:
Hermes → MiniCPM5 → one simple tool

Then:
Hermes → MiniCPM5 → several tools

Then:
+ persistent memory

Finally:
+ Laya routing

That makes debugging dramatically easier.
9. PowerShell installer logic
Your Windows-side deployment script should not try to build ARM images itself. It should orchestrate the ARM Kubernetes node.
The flow should be:
# 1. Kubernetes available?
kubectl cluster-info

# 2. ARM64 node exists?
kubectl get nodes -o wide

# 3. Helm available?
helm version

# 4. Create namespace
kubectl create namespace hermes `
  --dry-run=client -o yaml |
  kubectl apply -f -

# 5. Install/upgrade
helm upgrade --install hermes `
  ./hermes-stack `
  --namespace hermes `
  --wait `
  --timeout 15m

# 6. Status
kubectl get pods -n hermes

# 7. Real Hermes smoke test
kubectl exec -n hermes statefulset/hermes -- `
  hermes version

# 8. LLM health
kubectl exec -n hermes statefulset/hermes -- `
  curl -fsS http://llm:8080/v1/models

Then make the script fail if any of those checks fail.
That is much safer than:
helm says deployed
     ↓
declare success

10. Tool reliability is still the most important test
The research across Hermes and these small models continues to point to the same conclusion:
Do not benchmark only the LLM. Benchmark the complete protocol.
Your actual system is:
prompt
  ↓
model chat template
  ↓
LLM
  ↓
tool syntax
  ↓
llama.cpp/SGLang parser
  ↓
OpenAI-compatible response
  ↓
Hermes parser
  ↓
Hermes tool dispatcher
  ↓
actual tool
  ↓
result
  ↓
LLM

A 95%-accurate model connected through a broken parser can produce a 0%-useful agent.
That is particularly important because Hermes has had documented cases where model/tool behavior was affected by its enforcement configuration. Current Hermes configuration provides both tool_use_enforcement and execution_guidance, and its source explicitly recognizes Qwen in execution guidance while tool-use enforcement has different auto-selection behavior. GitHub
So I would build a regression suite of roughly 200 tool calls:
40 simple calls
30 nested JSON arguments
20 Unicode/string escaping
20 sequential tool chains
20 failed-tool recovery
20 memory operations
20 search → action workflows
10 long-context calls
10 malformed-input recovery
10 deliberate loop scenarios

Record:
syntax valid
schema valid
Hermes recognized call
tool actually executed
correct arguments
correct result interpreted
task eventually completed

The metric you care about is:
end-to-end successful tool execution, not merely valid-looking JSON.

Final architecture
For the low-resource ARM64 target we have been designing, I would deploy this first:
                    ARM64 Kubernetes
                          │
                    Hermes Agent
                    official image
                          │
                  OpenAI-compatible
                          │
                          ▼
                  llama.cpp server
                          │
                   MiniCPM5-2B
                     Q4_K_M
                     ctx 8K
                          │
              ┌───────────┴──────────┐
              │                      │
         Hermes tools          Hermes memory
              │
              │
        optional later
              ▼
         Laya ~421M
     structured decisions

Phase 1: Hermes + MiniCPM5-2B Q4_K_M.
Phase 2: run the 200-call tool regression suite.
Phase 3: compare LFM2.5-2.6B Q4_K_M using exactly the same suite.
Phase 4: enable native Hermes memory.
Phase 5: add Laya only for cheap routing/classification where it measurably saves main-model calls.
Phase 6: only move upward to Qwen3.5-4B/9B when the measured failure is reasoning quality rather than transport/tool syntax.
That gives you a much cleaner low-resource Hermes system than immediately deploying Qwen3.5-9B + vision model + Laya + external memory + Kubernetes all at once. The official Hermes image is now multi-architecture, but because of the documented ARM64 packaging incident I would treat hermes version/hermes doctor as mandatory deployment gates and pin the exact tested image digest afterward.