# Stage 15B Next Prompt

Recommended next slice: **Stage 15B Pre-Push Release Hardening**

Focus:
1. Re-run full build/test/lint verification.
2. Review `git status --short`, `git diff --stat`, and `git diff --check`.
3. Scan for secrets, production config drift, and accidental local values.
4. Review any remaining seed/demo scaffolding that could confuse a release.
5. Confirm no unrelated files are staged for future push.

Why this next:
- The remaining product work is now mostly polish.
- The safest high-value move is to lock down release hygiene before any push.

Do not execute this prompt automatically. Use it only as the next controlled stage.
