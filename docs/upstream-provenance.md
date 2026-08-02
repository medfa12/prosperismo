# Upstream provenance

Prosperismo began from a source snapshot of
[KytyPS5](https://github.com/KytyPS5/KytyPS5) on 2026-08-02.

- Upstream commit: `fb5ecec455cf6c67154134429485ffccbfc34203`
- Upstream subject: `renderer: ignore stale stencil state for depth-only targets`
- Import method: shallow clone with all pinned submodules, followed by removal
  of the KytyPS5 root Git history and initialization of Prosperismo's own
  repository.
- License at import: GPL-2.0-only. Original Kyty and third-party notices remain
  in `LICENSES/` and the corresponding dependency trees.

The dependency commits imported with that snapshot are:

| Dependency | Commit |
|---|---|
| LibAtrac9 | `ec8899dadf393f655f2871a94e0fe4b3d6220c9a` |
| SDL2 | `4b69833bc54abf3dd3288d4aa7afbba527775e5b` |
| SPIRV-Headers | `01e0577914a75a2569c846778c2f93aa8e6feddd` |
| SPIRV-Tools | `7f2d9ee926f98fc77a3ed1e1e0f113b8c9c49458` |
| Vulkan-Headers | `2fa203425eb4af9dfc6b03f97ef72b0b5bcb8350` |
| VulkanMemoryAllocator | `a1d434708c217b2a6c7b365f1fe41fa03a562e59` |
| ffmpeg-core | `94dde08c8a9e4271a93a2a7e4159e9fb05d30c0a` |
| fmt | `11ddbcb7898d2d3445d431a54814367b21dee6ad` |
| magic_enum | `1384769c66bd16ec9bb1353f45fe8ec8ccc12dbd` |
| nlohmann_json | `272411c5e6ea45919af7673524e74e60c62116df` |
| spdlog | `8671ca4d492c8ee1cdfd3dd88afb9f88dd268178` |
| tracy | `00c079cec91e6a374519aa5719a073e7f4539c9d` |
| xxHash | `e573d4d2aaeaba0f3e5a0a9a54144a1f2b4b56e7` |

Sony SDKs, firmware, symbols, games, decompiled bundles and captured evidence
are not part of the source import and must remain under the ignored
`ps5oracle/` and `games/` roots.
