# Hermes 3 ARM inference validation — 2026-10-01

Deployed release hermes revision 5 on ARM64 Kubernetes. Native llama.cpp build 11312 (Linux aarch64), 2 threads, batch/ubatch 512, context 8192, one slot, CPU limit 2.

Model: NousResearch/Hermes-3-Llama-3.2-3B-GGUF, immutable revision 3cd927095d8cbab12c743f932aa63b6f7bbfa141. Q4_K_M file SHA256 91776fe0f6cd7483d9d5e06162fdd1f8f0262c15ced269791b4d96a655e8a5a2 verified by download init container. Previous MiniCPM model remains cached for rollback.

Identical raw short prompt, temperature 0, seed 42, 96 generated tokens, EOS ignored to keep output length fixed, prompt caching disabled:
- MiniCPM5-2B: one baseline run, generation 8.25 tokens/s, prompt 17.99 tokens/s.
- Hermes 3: three runs, generation 5.75 / 5.80 / 5.82 tokens/s; mean 5.79 tokens/s. Different tokenizers produced 10 versus 12 prompt tokens.
- Hermes long prompt: 1306 prompt tokens, 96 output tokens; prompt 13.52 tokens/s (96.60 s), generation 4.11 tokens/s (23.13 s).

A separate chat-template request asked for only the number for 2+2 and returned exactly 4. Raw completion outputs are performance measurements, not chat-quality tests. Short benchmarks are small samples, not a statistical reliability certification. This deployment does NOT establish faster inference than MiniCPM; measured short generation is about 30% slower. No GPU or verified KleidiAI acceleration is claimed.

Raw timing/response evidence: artifacts/benchmark-minicpm-baseline.json, artifacts/benchmark-hermes3-short.json, artifacts/benchmark-hermes3-long.json, artifacts/hermes3-answer-validation.json. Integration report: artifacts/hermes3-deployment-results.json (check its passed field; do not assume success from this document).

Final integration validation: FAILED. Model discovery/configuration/inference, authentication rejection, health and authenticated WebUI chat passed. Hermes arithmetic chat and real terminal-tool execution failed their assertions. See artifacts/hermes3-deployment-results.json. Native ARM optimization confirmed by loaded /app/libggml-cpu-armv8.2_2.so. No OOM events observed. The requested model remains deployed, but this is not a production-ready agent validation. Rollback to prior model is available with Helm revision 4; not executed because the requested model must remain deployed unless the user chooses otherwise.
