# Stage 15C Pre-Commit Verification Plan

```bash
dotnet build backend-dotnet/Opstrax.Api.csproj
dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore

cd frontend
npm run build
npm run lint
cd ..

if backend/package.json exists:
  cd backend
  npm run build
  cd ..

git diff --check
git status --short
git diff --cached --stat
git diff --cached --check
```

Notes:
- Run the build/test/lint commands after staging only the approved files.
- Do not proceed to commit until the cached diff is clean and secrets/generated files are still excluded.

