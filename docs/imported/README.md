# Imported SharpEmu research documents

This directory preserves the complete tracked `docs/` snapshots that informed the Prosperismo migration. These are immutable research snapshots, not automatically current Prosperismo claims. Curated Prosperismo documents remain separately maintained under `docs/sony-shell/` and elsewhere in `docs/`; this import does not overwrite them.

## Preserved snapshots

| Snapshot | Source repository | Source ref | Resolved commit | Imported payload | Manifest |
| --- | --- | --- | --- | ---: | --- |
| Former `sharpemu-home` shell/research lineage | `https://github.com/medfa12/sharpemu.git` | `codex/ps5-shell-integration` handoff commit | `d651a3b0a5e52147df84cbe559ae47021171d1cc` | 85 files in `sharpemu-home/` | `manifests/sharpemu-home.tsv` |
| Current SharpEmu master snapshot | `https://github.com/medfa12/sharpemu.git` | `master` | `273c3881fe384e78afd6d0f3e50bbcbb0f94c87e` | 54 files in `sharpemu-master/` | `manifests/sharpemu-master.tsv` |
| Current SharpEmu working-tree document overlay | local `C:\sharpemu` checkout | uncommitted overlay on `master` | base `273c3881fe384e78afd6d0f3e50bbcbb0f94c87e` | 1 file in `sharpemu-working-tree-overlays/` | `manifests/sharpemu-working-tree-overlays.tsv` |

Paths below each payload directory are relative to the source repository's `docs/` directory. Identical files are intentionally present in both snapshots; nothing was silently deduplicated.

The former `feat/ps5-home-shell` ref resolved to `225db3c1105765e988accede6fb14b19aa8ba8d7` with 75 tracked documents. A path comparison found it to be a strict subset of the 85-file handoff snapshot: it contributed no additional document path, while the handoff added ten. Therefore the handoff payload fully preserves that former branch without requiring a third duplicate snapshot.

## Integrity

Each manifest records the relative path, Git mode, source Git blob ID, and SHA-256 of the imported bytes. Verification against `git hash-object --no-filters` reported:

- `sharpemu-home`: 85 source files, 85 imported files, 0 blob mismatches.
- `sharpemu-master`: 54 source files, 54 imported files, 0 blob mismatches.
- `sharpemu-working-tree-overlays`: one modified tracked document, `minecraft-bringup.md`, preserved byte-for-byte at SHA-256 `df910391201872834337da5680e7e191021502bffc98c17e775d58cc44c11bc8`; its diff against master is +54/-0 lines.
- All 139 committed-snapshot payload files have mode `100644` and decode as strict UTF-8. The one working-tree overlay also decodes as strict UTF-8. No binary or encoding exception was encountered.

At overlay capture time, `git status --short -- docs` reported only `M docs/minecraft-bringup.md`; `git ls-files --others --exclude-standard -- docs` returned no untracked document. The overlay is intentionally separate from `sharpemu-master/` so it cannot mutate or obscure the byte-exact committed snapshot.

The archive operation explicitly disabled `core.autocrlf`; the payload bytes therefore match the committed Git blobs rather than a Windows CRLF checkout.
