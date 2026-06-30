# Stage 15B Final Verification and Pre-Push Prompt

Do not execute this prompt automatically.

## Objective

Verify every Stage 15A claim, scan for accidental secrets/config drift, confirm build/test/lint evidence, review generated artifacts, and prepare a safe pre-push plan without pushing.

## Required Checks

1. `git status --short`
2. `git diff --stat`
3. `git diff --check`
4. `dotnet build backend-dotnet/Opstrax.Api.csproj`
5. `dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore`
6. `npm run build` in `frontend/`
7. `npm run lint` in `frontend/`
8. `npm run build` in `backend/` if applicable
9. Review appsettings, env files, lockfiles, generated artifacts, and any `node_modules`, `bin`, or `obj` paths

## Expected Output

- Confirm what changed
- Confirm what remains partial
- Confirm rollback guidance
- Confirm whether the branch is safe to push

## Do Not

- Do not push
- Do not deploy
- Do not touch production

