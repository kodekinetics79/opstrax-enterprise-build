# Stage 5C P0-B1B Next Prompt

Before starting P0-B1B, add one real worker/dispatcher path on top of the now-persistent foundation:

- consume one pending outbox row and advance its status with retry metadata
- consume one inbox row and mark it processed with event-processing logging
- keep every action tenant-scoped and correlation-aware
- preserve fail-closed authorization behavior and do not reintroduce allow-by-default paths
- keep AI actions request-only unless an approval decision exists
- keep approval-required actions blocked from direct execution
- do not build Customer/Contract/Job/Trip/Revenue yet
- do not touch production
- do not push
- do not deploy

The current foundation slice is safe to continue from, but the next stage should prove runtime processing, not just schema persistence.
