# Bounded read-only staging load tests

This lane performs exactly two `GET` requests per iteration: public liveness and one authenticated tenant summary. There are no mutation methods. The runner requires HTTPS, an exact staging-host allowlist match, a disposable/isolated staging acknowledgement, and a bearer credential loaded from an ignored mode-0600 `.env.local`. Both known production hosts are refused by default.

| Profile | Iterations/s | Maximum HTTP requests/s | Duration | Max VUs |
| --- | ---: | ---: | ---: | ---: |
| smoke | 1 | 2 | 30s | 4 |
| load | 5 | 10 | 300s | 20 |
| stress | 10 | 20 | 600s | 50 |

Values may be lowered but cannot exceed their profile cap. The global hard cap is therefore 20 HTTP requests/s, 10 minutes, and 50 virtual users.

```bash
install -m 600 tests/load/.env.local.example tests/load/.env.local
node --test tests/load/test_load_guard.mjs
node tests/load/run_load.mjs --dry-run
node tests/load/run_load.mjs --execute
```

Actual execution additionally requires a separately installed, approved k6 binary. CI blocks on guard/static tests only; it does not contact staging or consume secrets. No load/stress run was executed during this rebuild.
