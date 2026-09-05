# AB Export And Hotfix E2E: Local And Cloudflare

> **Status**: Delivery verified; LuaIndex P0 and Full startup closed by S1; Local Hotfix/Cloudflare runtime gates pending rerun
> **Date**: 2026-07-15
> **Scope**: Real Unity AB Full/Hotfix export, local delivery, Windows Player runtime update, Cloudflare Pages delivery, and remote runtime update

## Goal

Prove the current AB build and hotfix chain end to end with fresh artifacts. The test must cover a local-only server and the `fyasset` Cloudflare Pages project. Existing AA test artifacts may be deleted. Runtime or build code defects are diagnosed and reported, not fixed without a separate approval.

## Approved Operations

- Delete existing AA test packages, repository state, ServerData, and StreamingAssets bootstrap artifacts.
- Switch FYAsset routing to AB and use Unity 2022.3.62f3.
- Start and stop a localhost-only server on `127.0.0.1:54321`.
- Build and run hidden Windows test Players, then terminate them after condition-based log verification.
- Temporarily change one collected Lua asset to force a real Hotfix Bundle delta, then restore the source asset.
- Preflight and deploy the complete `HotfixPublish/Cloudflare` service root to Cloudflare Pages project `fyasset`.

## Execution Checklist

1. Record the dirty-worktree baseline and clear AA-only artifacts.
2. Configure AB, local Hotfix URL, and the matching Unity editor.
3. Run AB Full from the reset AB repository and verify package, manifest, repository HEAD, Bootstrap, and StreamingAssets.
4. Publish the Full package locally, build a Windows Player, and verify clean baseline startup.
5. Add a temporary Lua marker, run AB Hotfix, verify a non-empty cumulative delivery, publish locally, and verify forward update in the same runtime root.
6. Restore the Lua source, switch the configured Hotfix URL to Cloudflare, and build a second Player with the original Full baseline.
7. Compare remote metadata with the local Cloudflare mirror, verify Wrangler identity, deploy only when the preflight is safe, and verify a clean remote forward update.
8. Stop all test processes, verify no temporary marker remains, collect logs/hashes/manifests, and report severe findings without code fixes.

## Evidence Gates

- Unity commands exit successfully and logs contain no compiler/build failure.
- Full and Hotfix `PackageIndex.json` files use backend `AB` and expected versions.
- `ABManifest.json`/`.bin` and every declared Bundle exist and pass size/CRC validation.
- The Hotfix delivery is non-empty and the runtime downloads and activates the new package.
- Local and Cloudflare runtime logs reach `GameLauncher` completion with exactly one Hotfix completion.
- Cloudflare metadata is no-store, versioned package files are immutable, and downloaded files match the local mirror by SHA-256.
- The local server and test Players are no longer running when the test ends.

## 2026-07-19 Closure Boundary

- `plan-lua-resource-boundary-separation-20260719.md` S1 owns the LuaIndex P0 fix and clean Full Player startup verification.
- S1 verification does not retroactively satisfy this plan's Local Hotfix or Cloudflare runtime-update gates.
- Re-running those network/publication gates requires separate developer approval; no external request or deployment belongs to S1.
