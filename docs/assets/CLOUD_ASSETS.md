# Lute Cloud Asset Registry

This file is the authoritative registry of S&Box cloud assets used by Lute.
The `sbox/.sbox/cloud/` directory is disposable cache — it is NOT evidence
that Lute depends on an asset. The cloud ident (publisher.asset_name) is
the source of truth.

## Rules

- Cloud assets are referenced by their S&Box ident, not by cache filenames.
- Core Lute assets use compile-time `Cloud.Model("ident")` / `Cloud.Material("ident")`.
- Runtime `await Cloud.Load<T>()` is reserved for dynamic UGC content only.
- A cloud model is NOT construction authority — the blueprint is authoritative.
- `.sbox/cloud/` is fully gitignored and disposable.

## Registered Assets

| ident | type | purpose | verified_scale | date_checked |
|-------|------|---------|----------------|--------------|
| (none registered yet) | | | | |

To register a new asset, add a row above and run `python agent/cloud_assets.py verify <ident>`.
