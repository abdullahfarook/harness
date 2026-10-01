"""Real HTTP protocol tests; credentials are environment-only and never logged."""
import json
import os
import time
import urllib.error
import urllib.request
import uuid

LLM = os.environ["LLM_URL"]
HERMES = os.environ["HERMES_URL"]
UI = os.environ["UI_URL"]
KEY = os.environ["API_SERVER_KEY"]
MODEL = os.environ.get("MODEL_ALIAS", "MiniCPM5-2B")
CONTEXT = int(os.environ.get("MODEL_CONTEXT", "8192"))
REPORT = {"checks": [], "timestamp_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}


def request(url, body=None, key=None, raw=False):
    headers = {"Content-Type": "application/json"}
    if key:
        headers["Authorization"] = "Bearer " + key
    req = urllib.request.Request(url, data=json.dumps(body).encode() if body is not None else None, headers=headers)
    with urllib.request.urlopen(req, timeout=900) as response:
        data = response.read().decode()
        return data if raw else json.loads(data)


def check(name, action):
    start = time.monotonic()
    try:
        action()
        REPORT["checks"].append({"name": name, "passed": True, "seconds": round(time.monotonic() - start, 2)})
        print("PASS " + name, flush=True)
    except Exception as exc:
        # Never serialize request bodies, auth headers or password-bearing responses.
        REPORT["checks"].append({"name": name, "passed": False, "error_type": type(exc).__name__, "seconds": round(time.monotonic() - start, 2)})
        print("FAIL " + name + " (" + type(exc).__name__ + ")", flush=True)


def require(condition):
    if not condition:
        raise AssertionError("Expectation not satisfied")


def auth_rejection():
    for key in (None, "deliberately-invalid-test-key"):
        try:
            request(HERMES + "/v1/models", key=key)
        except urllib.error.HTTPError as exc:
            require(exc.code in (401, 403))
        else:
            raise AssertionError("Unauthenticated request accepted")


def model_configuration():
    props = request(LLM + "/props")
    require(props["default_generation_settings"]["n_ctx"] == CONTEXT)
    require(props["total_slots"] == 1)
    require(props["model_alias"] == MODEL)
    REPORT["model_configuration"] = {"context": CONTEXT, "slots": props["total_slots"], "quantization": props["model_ftype"], "path": props["model_path"]}


def completion(base, model, key=None):
    response = request(base + "/v1/chat/completions", {
        "model": model, "messages": [{"role": "user", "content": "Reply briefly: what is 2 plus 2?"}],
        "stream": False, "max_tokens": 1024
    }, key)
    content = response["choices"][0]["message"].get("content", "").strip()
    require(bool(content) and ("4" in content or "four" in content.lower()))
    if base == HERMES:
        REPORT["hermes_runtime"] = response.get("runtime", {})


def ui_protocol():
    require("<html" in request(UI + "/", raw=True).lower())
    config = request(UI + "/api/config")
    require(config["features"]["auth"] is True)
    require(config["features"]["enable_signup"] is False)
    auth = request(UI + "/api/v1/auths/signin", {"email": os.environ["WEBUI_ADMIN_EMAIL"], "password": os.environ["WEBUI_ADMIN_PASSWORD"]})
    token = auth["token"]
    require(auth["role"] == "admin")
    models = request(UI + "/api/models", key=token)["data"]
    require(len(models) > 0)
    model = models[0]["id"]
    response = request(UI + "/api/chat/completions", {
        "model": model, "messages": [{"role": "user", "content": "Reply in a short sentence: say hello."}],
        "stream": False, "params": {"max_tokens": 1024}
    }, token)
    require("hello" in response["choices"][0]["message"].get("content", "").lower())
    REPORT["webui_model"] = model


def tool_execution():
    token = uuid.uuid4().hex
    marker = "/opt/data/hermes-smoke-" + token + ".txt"
    REPORT["tool_marker"] = {"path": marker, "value": token}
    response = request(HERMES + "/v1/responses", {
        "model": "hermes-agent",
        "input": "Use the terminal tool to execute this exact shell command: printf '%s' '" + token
                 + "' > " + marker + "; cat " + marker
                 + ". You must actually run the command, not just describe it. Then answer briefly.",
        "store": True
    }, KEY)
    output = response.get("output", [])
    require(any(item.get("type") == "function_call" for item in output))
    require(any(item.get("type") == "function_call_output" and token in str(item.get("output", "")) for item in output))
    REPORT["tool_response_id"] = response.get("id")


def main():
    check("llm_health", lambda: require(request(LLM + "/health")["status"] == "ok"))
    check("model_context_and_single_slot", model_configuration)
    models = request(LLM + "/v1/models")["data"]
    check("llm_model_discovery", lambda: require(any(item["id"] == MODEL for item in models)))
    check("llm_inference", lambda: completion(LLM, MODEL))
    check("hermes_health", lambda: require(request(HERMES + "/health")["status"] == "ok"))
    check("hermes_auth_rejection", auth_rejection)
    check("hermes_models", lambda: require(bool(request(HERMES + "/v1/models", key=KEY)["data"])))
    check("hermes_chat", lambda: completion(HERMES, "hermes-agent", KEY))
    check("webui_health", lambda: request(UI + "/health"))
    check("webui_authenticated_chat", ui_protocol)
    check("hermes_real_tool_protocol", tool_execution)
    REPORT["passed"] = all(item["passed"] for item in REPORT["checks"])
    print("REPORT_JSON=" + json.dumps(REPORT), flush=True)
    raise SystemExit(0 if REPORT["passed"] else 1)


if __name__ == "__main__":
    main()
