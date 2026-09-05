# Draft: Hotfix Publish Integrity and Post-Deploy Audit

## Status

Archived / Superseded 2026-07-14.

The local exact-name/size/CRC validation is owned by
`../plan-hotfix-review-hardening-20260714.md`. The deployed AA package already received byte-for-byte and cache-policy
audits on 2026-07-12 and 2026-07-13. Automated remote rollback/sampling remains intentionally unplanned until a real
failure requires it.

## Problem

The current publish transaction verifies only the expected manifest file exists before replacing a backend mirror. It does not prove that a package is internally complete or that a remote deployment serves immutable versioned bytes matching the repository commit.

## Candidate Scope

- Parse the AA or AB manifest before Push.
- Require the AA catalog for AA packages.
- Verify every declared file exists and matches declared size, hash, and CRC where available.
- Reject unexpected duplicate bundle identities and backend/version mismatches.
- Verify the deployed PackageIndex, manifest, catalog, and Bundles through HTTP after a Cloudflare Push.
- Audit versioned package URLs for immutable caching and byte-for-byte identity.
- Keep deployment verification transactional where the provider supports rollback; otherwise surface the mirror/deployment divergence explicitly.

## Open Decisions

- Whether strong validation belongs in the build commit, Push transaction, or both.
- Whether a remote verification failure should automatically redeploy the previous mirror or only block publication sign-off.
- Whether to sample large Bundles or hash every remote file.
- How AB manifest-level `FileHash` participates in repository and remote integrity checks after schema v4.
