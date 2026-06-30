# Stage 15B Git Push Guidance

Current branch: `opstrax-product-main`

Recommended pre-push commands:
1. `git status --short`
2. `git diff --stat`
3. `git diff --check`
4. `dotnet build backend-dotnet/Opstrax.Api.csproj`
5. `dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore`
6. `cd frontend && npm run build && npm run lint`

Recommended commit grouping:
- Keep backend/runtime changes, frontend UI changes, tests, and docs in separate commits where practical.
- Do not stage generated artifacts, `mobile/node_modules`, `frontend/node_modules`, `frontend/dist`, or local `.env`.

Recommended add strategy:
- Use explicit pathspecs for source files and docs.
- Avoid `git add .` in this worktree because the tree contains untracked/generated content.

Push guidance:
- Guidance only: `git push origin opstrax-product-main`
- Do not force-push unless that was explicitly intended.

