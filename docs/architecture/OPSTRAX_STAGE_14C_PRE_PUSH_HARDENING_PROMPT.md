# Stage 14C Pre-Push Hardening Prompt

Do not execute this now. Use it only after Stage 14B verification passes.

## Scope

Perform pre-push hardening on the local worktree:
- inspect the full git diff and group changes by stage/module
- run secret-pattern scans
- inspect appsettings / env files
- inspect migrations
- inspect package lock changes
- inspect generated artifacts
- check `node_modules`, `bin`, `obj`, and build outputs
- verify backend / frontend / Node builds, tests, and lint
- produce a commit grouping plan
- produce a rollback plan
- prepare safe Git push guidance

## Rules

- Do not push automatically
- Do not deploy
- Do not touch production
- Do not introduce new scope
- Do not hide unfinished items

