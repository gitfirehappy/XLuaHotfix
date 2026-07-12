# Hotfix URL and Isolated Publish Targets

## Status

Implemented, verified locally and remotely, and signed off on 2026-07-12.

## Summary

Complete the AA URL/server integration without building 4.0.1, restoring AB, or changing same-version hotfix behavior. Local and Cloudflare publication use backend-isolated roots so AA and AB never share one `PackageIndex.json`.

## Approved Changes

1. Add Local and Cloudflare Pages Push targets with a public base URL and publish each backend under `{TargetRoot}/{AA|AB}`.
2. Keep URL changes explicit: selecting a target may apply its derived AA/AB URL, while Push itself never edits runtime settings.
3. Reuse `FYAssetSettings.ProjectName` as the Wrangler Pages project name, use production branch `main`, and require `wrangler` on `PATH`.
4. Add an Editor-controlled localhost server for the Local target, bound to `127.0.0.1`, with token-authenticated health/shutdown and no-cache metadata responses.
5. Configure Local at `HotfixPublish/Local` and Cloudflare at `HotfixPublish/Cloudflare`; keep generated mirrors out of Git.

## Acceptance

- Local AA Push writes `HotfixPublish/Local/AA/PackageIndex.json` and the current AA package under `AA/Packages/`.
- AB uses a separate `AB/PackageIndex.json`; an absent AB HEAD remains blocked without touching AA output.
- Applying the Local target writes `http://127.0.0.1:18080/AA/` to AA settings only.
- The local server survives Play Mode reload, reports health, returns no-store headers for pointer/manifest/catalog files, and can be stopped safely.
- Cloudflare Push stages the isolated mirror, writes Pages cache headers, invokes Wrangler only after explicit Push, and rolls back the mirror when deployment fails.
- Current AA 4.0.0 completes a real localhost download and `TestDialogue` resource-load smoke after project-scoped cache cleanup.

## Remote Acceptance Evidence

- On 2026-07-12, the developer explicitly authorized real Wrangler, Cloudflare Pages, and Unity E2E operations.
- Wrangler 4.110.0 deployed the isolated mirror to the `my-game-xlua-hotfix` Pages project on production branch `main`.
- `https://firehappy-cfy.com/AA/` served PackageIndex, AAManifest, catalog, and all seven bundles with byte-for-byte SHA-256 matches against the local mirror.
- PackageIndex returned `Cache-Control: no-store`; versioned package files returned `public, max-age=31536000, immutable`; `/AB/PackageIndex.json` returned 404.
- A clean batchmode `TestDialogue` run downloaded all seven bundles and catalog over HTTPS, loaded the external catalog with 93 keys, initialized 19 AA entries, and displayed the expected Lua dialogue text.
- Wrangler initially timed out because Node 24 did not inherit the Windows system proxy and direct IPv6 connection setup exceeded Undici's 10-second timeout. Per-process `HTTP_PROXY`, `HTTPS_PROXY`, and `NODE_USE_ENV_PROXY=1` resolved the deployment without persistent machine changes.

## Boundaries

- Do not build AA 4.0.1 or any AB package.
- Do not change runtime hotfix comparison, catalog activation, loading contracts, or package formats.
- Do not install, authenticate, or invoke Wrangler against Cloudflare during automated implementation acceptance. The later real deployment was a separate developer-authorized E2E operation recorded above.
- Treat all test publish mirrors and persistent test caches as disposable.
