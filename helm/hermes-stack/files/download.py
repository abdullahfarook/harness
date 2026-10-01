"""Download immutable GGUF with checksum verification and atomic cache updates."""
import hashlib
import os
from pathlib import Path
import time
import urllib.request


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def download():
    target = Path("/models") / os.environ["MODEL_FILE"]
    expected = os.environ["MODEL_SHA256"]
    if target.exists() and digest(target) == expected:
        print("Verified existing model cache", flush=True)
        return
    partial = target.with_suffix(".partial")
    url = ("https://huggingface.co/" + os.environ["MODEL_REPO"] + "/resolve/"
           + os.environ["MODEL_REVISION"] + "/" + target.name)
    for attempt in range(4):
        try:
            with urllib.request.urlopen(url, timeout=120) as response, partial.open("wb") as output:
                while chunk := response.read(4 * 1024 * 1024):
                    output.write(chunk)
            if digest(partial) != expected:
                raise ValueError("GGUF checksum mismatch")
            partial.replace(target)
            target.chmod(0o644)
            print("Downloaded and SHA256-verified model", flush=True)
            return
        except Exception:
            partial.unlink(missing_ok=True)
            if attempt == 3:
                raise
            time.sleep(5 * (attempt + 1))


if __name__ == "__main__":
    download()
