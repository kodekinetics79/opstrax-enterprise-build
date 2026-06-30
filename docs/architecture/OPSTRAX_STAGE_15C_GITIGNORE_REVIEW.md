# Stage 15C .gitignore Review

| Pattern | Present? | Needed? | File | Recommendation |
|---|---|---|---|---|
| `.env` | Yes | Yes | `.gitignore` | Keep ignoring the local environment file. |
| `.env.local` | No before this pass | Yes | `.gitignore` | Added safely to prevent local overrides from leaking. |
| `.env.*` | No before this pass | Yes | `.gitignore` | Added with `!.env.example` exception so examples remain trackable. |
| `!.env.example` | No before this pass | Yes | `.gitignore` | Added to preserve sample env docs. |
| `node_modules/` | Yes | Yes | `.gitignore`, `frontend/.gitignore`, `mobile/.gitignore` | Keep in all relevant layers. |
| `dist/` | Yes | Yes | `.gitignore`, `frontend/.gitignore`, `mobile/.gitignore` | Keep ignoring build output. |
| `build/` | Added in root review | Yes | `.gitignore` | Safe to ignore common build output. |
| `coverage/` | Added in root review | Yes | `.gitignore` | Safe to ignore test coverage output. |
| `bin/` | Yes | Yes | `.gitignore` | Keep ignoring .NET build output. |
| `obj/` | Yes | Yes | `.gitignore` | Keep ignoring .NET intermediate output. |
| `.cache/` | Added in root review | Yes | `.gitignore` | Safe to ignore local cache artifacts. |
| `*.log` | Added in root review | Yes | `.gitignore` | Safe to ignore logs. |
| `.DS_Store` | Yes | Yes | `.gitignore`, `frontend/.gitignore`, `mobile/.gitignore` | Keep ignoring macOS metadata. |
| `tmp/` / `temp/` | Added in root review | Yes | `.gitignore` | Safe to ignore temp directories. |
| `.vercel` | Yes | Yes | `.gitignore`, `frontend/.gitignore` | Keep ignoring deployment output. |

