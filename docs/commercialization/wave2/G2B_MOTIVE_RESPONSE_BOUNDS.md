# G2B Motive response-size hardening — 2026-09-02

Local follow-up to reviewed PR #118 head
`b4bdf5a42aa78fab20905a20b7538ca7bcbaae5d`, isolated on
`wave2/motive-response-bounds`. This does not change PR #118's exact-head evidence,
deploy an application, configure a provider, or close a commercialization gate.

## Observation and fix

Independent AppSec review identified unbounded response buffering in the Motive
token exchange and nine read-only scope probes. Default `HttpClient` completion
buffers response content before the application receives it; reading a string
afterward does not impose a byte bound.

The follow-up uses headers-only completion and a shared bounded JSON reader:

- Token response: 64 KiB maximum; read-only probe response: 1 MiB maximum.
- `Content-Length` is an early rejection hint, never trusted as an actual bound.
  Unknown, chunked, or understated bodies stop after at most limit-plus-one bytes.
- The reader bounds its byte buffer before parsing UTF-8 JSON, including multibyte
  characters split across reads. Response and stream resources are disposed.
- Failed HTTP statuses are handled without reading or buffering their bodies.
- Linked cancellation covers body reads: 20 seconds per exchange/probe and the
  existing 25-second overall verification budget. Caller cancellation also stops
  reading. Headers-only completion by itself does not cover content timeouts.
- Oversized or cancelled responses fail closed with fixed operator messages,
  no echoed provider body, no accepted tokens, and no continued scope probes.

These are OpsTrax defensive limits for a small smoke-test response, not claimed
Motive contract limits. A legitimate larger response will fail the smoke test;
that requires evidence and a reviewed adjustment, not bypassing the guard.

Framework reference: [HttpCompletionOption timeout and buffering behavior](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-8.0).

## Verification scope

The focused suite covers exact-byte boundaries, short UTF-8 reads, missing and
dishonest lengths, endless/stalled streams, malformed/empty JSON, rejected HTTP
statuses, ordinary success, and manual plus automatic post-header cancellation.
HTTP doubles deliberately throw if the client attempts to prebuffer content.
The automatic deadline case runs token and probe operations concurrently with
no caller cancellation and a bounded test watchdog.

PostgreSQL callback regressions add oversized exchange and probe outcomes to the
existing credential-clear/audit assertions. Each test owns a random schema in a
disposable loopback database. Provider responses remain simulated; these tests
do not establish live connectivity, production-role RLS, hardware behavior, or
regulatory compliance.

Final local results: 48/48 focused tests, 22/22 PostgreSQL callback tests, and
2,174/2,174 non-database regressions passed, with zero skips. The focused cases
are included in the non-database count and must not be added to it. Independent
AI SDET reproduced the 48 and 22 case suites; independent AI AppSec reviewed
the final source and tests and found the prior unbounded-response issue resolved.
Both reviews support local code readiness only, not qualified-human acceptance.

The automatic timeout test completed in 20 seconds. It proves autonomous
stalled-body termination but does not separately time the 25-second aggregate
budget across multiple slow successful probes; that aggregate limit is retained
and source-verified. Hosted CI and exact-SHA deployed journeys are still required
before any release claim for this new follow-up.

## Deployment boundary

PR #118 itself has eleven successful hosted checks in
[run 33659778571](https://github.com/kodekinetics79/opstrax-enterprise-build/actions/runs/33659778571).
Its merge remains separately blocked because production Render service
`Osptrax Fleet Management` auto-deploys `main` on commit; the requested
deployment-safeguard approval has not been received. No settings were changed.
Frozen G1A frontend/API remains
`e2230425a8e14249d2c0f477a7ec7b713a6ab27e`. Neither this local fix nor a future
code merge constitutes provider, pilot, ELD, or device certification.
