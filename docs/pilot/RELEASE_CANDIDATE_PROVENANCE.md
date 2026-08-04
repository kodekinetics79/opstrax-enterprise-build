# Release-candidate provenance and SBOM contract

This contract separates locally reproducible facts from evidence that can exist
only after CI or an authorized registry publication. It must not be used to turn
a dirty local build into a release authorization.

## Deterministic candidate bundle

Run from a clean candidate checkout:

```bash
tools/collect-release-candidate-provenance.sh \
  --output artifacts/release-provenance \
  --image api=opstrax-api:candidate \
  --image frontend=opstrax-frontend:candidate \
  --image gateway=opstrax-telematics-gateway:candidate
```

The collector fails on a dirty worktree by default and refuses to overwrite an
existing bundle. `--allow-dirty` is diagnostic-only: the manifest records the
dirty-path count and worktree-status hash, so that bundle cannot satisfy RC-01.

The bundle contains:

- exact commit, Git tree and commit time;
- deterministic `git archive` SHA-256 for the committed source;
- sorted per-file migration and dependency-lock/project hashes;
- content-addressed local image IDs and local RepoDigests;
- a CycloneDX JSON SBOM for each supplied image, generated with the digest-pinned
  Trivy 0.70.0 image;
- SHA-256 indexes covering every artifact.

Local image IDs are immutable identities for the images tested on that Docker
daemon. They are not proof that the deployable images were published. A
published registry reference must be supplied explicitly as
`--published-digest component=registry/name@sha256:<64 hex>`; the
`--require-registry-digest` option fails if any image lacks that evidence.

## Exact-SHA CI artifact

The blocking `exact-sha-release-evidence` CI job runs only after frontend, Node,
.NET, Postgres integration and production-container jobs succeed. It downloads
the container/SBOM provenance from the same `github.sha`, verifies that commit,
records every mandatory job result, hashes the combined evidence and uploads
`opstrax-release-candidate-<full-git-sha>`.

The GitHub Actions summary records the uploaded artifact digest. A release
reviewer must retain the run URL, run attempt, artifact name/digest and downloaded
bundle hash in the Safety evidence index. Repository workflow source proves the
mechanism exists; only a completed external run proves RC-02.

## Externally dependent gates

The repository cannot itself prove:

- that the exact-SHA workflow completed successfully in GitHub;
- that images were published to the approved registry and the recorded
  RepoDigests are retrievable;
- that those exact digests were deployed to the rehearsal environment;
- registry signing/attestation policy, retention or admission-control results;
- vulnerability-database freshness at release time.

Those remain `NOT EVIDENCED` until immutable external references are attached.
