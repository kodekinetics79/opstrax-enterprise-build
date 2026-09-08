# CR-2026-09-03-01 — Controlled Wave 2 / Wave 3 overlap

| Field | Record |
|---|---|
| Classification | Class 2 sequence/concurrency change; no capability promotion and no commercial waiver of provider/hardware/regulatory evidence |
| Authority | Explicit CTO/program-owner instruction on 2026-09-03: "Complete Wave 2 and step into Wave 3 now in parallel." |
| Entry main | `aba2636c543c6f77cb47597383d4c2c8c32e61c8` |
| Supersedes | The v2.1 rule that no new Wave 3 implementation begins before Wave 2 formal exit; all other v2.1 evidence and commercial-truth rules remain binding |
| G2A | #115 remains PILOT / external-closure HOLD/open; engineering batch landed, real Samsara account/provider/Chrome/scale/assurance evidence still mandatory |
| G2B | #116 remains DEVELOPMENT/ROADMAP / external-closure HOLD/open; Samsara primary candidate, real commercial/provider/device/regulatory evidence still mandatory |
| G3A | #128 activated on branch `wave3/g3a-hos-workflow` for HOS operational workflow engineering/readiness; no regulated release claim ahead of certified source evidence |
| G3B | #129 activated on branch `wave3/g3b-dual-camera` for dual-facing camera integration engineering/readiness; no camera production claim ahead of real provider/hardware/privacy evidence |
| Concurrency rule | At most two engineering-intensive implementation lanes at a time. G2A/G2B may stay open in external-evidence HOLD while G3A/G3B consume the engineering lanes. If Wave 2 evidence returns and requires material engineering, pause/narrow a Wave 3 lane before adding material work. |
| P0 rule | Appendix B and two-independent-perspective rule remain mandatory for regulatory, tenant isolation, hardware/video privacy and final release claims |
| Wave 4+ | Locked |
| Capability effect | None at activation. Fleet/Telematics PILOT; GT06 PILOT/not certified; Samsara PILOT; HOS structures DEVELOPMENT; Certified ELD/HOS ROADMAP; dual-facing camera ROADMAP; Video Safety ROADMAP |

## Rationale

The Wave 2 source/readiness batch is materially advanced, but indispensable provider, commercial-rights and regulatory evidence is external. Treating those dependencies as passes would violate the program's truth rules. Keeping all engineering idle until those parties respond would not accelerate commercialization. This change therefore separates **open external certification gates** from **engineering-intensive active lanes** and authorizes bounded Wave 3 implementation without falsifying Wave 2 completion.

## Non-negotiable boundaries

1. #115/#116 do not close until their original real-world acceptance evidence passes.
2. No provider/ELD/camera/hardware claim is inferred from code, mocks, schemas, documentation or AI review.
3. G3A provider-dependent automatic-driving/inspection behavior stays fail-closed/unavailable until a certified source is proven.
4. G3B playable-video/provider/hardware behavior stays provider-pending until authentic events/clips are proven.
5. Implementation teams do not self-certify; qualified independent assurance remains required.
6. Exact-SHA deploy/retest, visible Chrome, real persisted data, failure/recovery and commercial-truth controls remain binding.
