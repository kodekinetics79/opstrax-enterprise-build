#!/usr/bin/env python3
"""PreToolUse guard: block Write/Edit/NotebookEdit outside this repo root.

Protects against an OpsTrax session accidentally modifying a sibling project
(e.g. ~/Downloads/zayra-ai-workforce). Relative paths are always allowed
(they resolve inside the project); absolute paths must stay under the repo root.
Fails open on any parse error so it never blocks legitimate in-repo work.
"""
import sys, json, os


def main() -> None:
    try:
        data = json.load(sys.stdin)
    except Exception:
        sys.exit(0)

    if data.get("tool_name", "") not in ("Write", "Edit", "NotebookEdit"):
        sys.exit(0)

    tool_input = data.get("tool_input", {}) or {}
    target_raw = tool_input.get("file_path") or tool_input.get("notebook_path")
    if not target_raw:
        sys.exit(0)

    root = os.path.realpath(os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd())
    target = target_raw if os.path.isabs(target_raw) else os.path.join(root, target_raw)
    target = os.path.realpath(target)

    if target == root or target.startswith(root + os.sep):
        sys.exit(0)  # inside repo -> allow

    reason = (
        "Blocked by repo-boundary guard: this edit targets a path OUTSIDE the OpsTrax repo.\n"
        f"  target: {target}\n"
        f"  repo:   {root}\n"
        "This OpsTrax session must not modify sibling projects (e.g. zayra-ai-workforce). "
        "If this is intentional, open a session rooted in that project instead."
    )
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }))
    sys.exit(0)


main()
