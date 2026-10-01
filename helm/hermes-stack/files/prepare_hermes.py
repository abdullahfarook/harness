"""Merge managed config and apply an explicit small-context compatibility patch.

The official v2026.9.14 minimum is 64K. This opt-in deployment keeps the actual
window truthful at 8K, limits tools, and changes only the minimum-window constant.
The installed application tree is not copied onto a PVC or made writable.
"""
import ast
import os
from pathlib import Path
import shutil
import yaml


def merge(existing, managed):
    for key, value in managed.items():
        if isinstance(value, dict) and isinstance(existing.get(key), dict):
            merge(existing[key], value)
        else:
            existing[key] = value
    return existing


def reconcile():
    target = Path("/opt/data/config.yaml")
    existing = yaml.safe_load(target.read_text()) or {} if target.exists() else {}
    if target.exists():
        shutil.copy2(target, str(target) + ".pre-helm")
    managed = yaml.safe_load(Path("/config/config.yaml").read_text())
    temporary = target.with_suffix(".helm-tmp")
    temporary.write_text(yaml.safe_dump(merge(existing, managed), sort_keys=False))
    os.chown(temporary, 10000, 10000)
    temporary.chmod(0o600)
    temporary.replace(target)


def patch():
    source = Path("/opt/hermes/agent/model_metadata.py").read_text()
    old = "MINIMUM_CONTEXT_LENGTH = 64_000"
    if source.count(old) != 1:
        raise RuntimeError("Pinned Hermes context guard changed; refusing unaudited patch")
    context = int(os.environ["SMALL_CONTEXT_LENGTH"])
    if not 1024 <= context < 64000:
        raise ValueError("Small-context patch requires a context from 1024 to 63999")
    patched = source.replace(old, "MINIMUM_CONTEXT_LENGTH = " + str(context), 1)
    ast.parse(patched)
    Path("/compat/model_metadata.py").write_text(patched)
    print("Applied explicit minimum-context compatibility patch: " + str(context), flush=True)


if __name__ == "__main__":
    reconcile()
    if os.environ.get("ALLOW_SMALL_CONTEXT") == "true":
        patch()
