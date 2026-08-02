# Astro GPU alignment handoff

Date: 2026-08-02

Status: investigation checkpoint only. The parallel Astro worker was stopped at
the user's request and produced no source changes. No Astro boot, nonblack final
frame or GPU fix is claimed by this handoff.

## Established boundary

- SharpEmu captured the full-resolution HDR scene target at guest address
  `0x514080000` with 869,977 nonblack pixels. The scene does render.
- The downstream 960x540 G-buffer/postprocess inputs examined at the clustered
  lighting boundary were all zero and were DCC-flagged.
- The final tone-map pixel shader had complete scalar constants
  (`smem_zero_filled=0`) and inherited black input. It was not the first stage
  where pixels became zero.
- Sony's `agc-registerstructs.natvis` identifies `CB_COLOR_CONTROL` mode 6 as
  `kDccDecompress`. Dropping that operation is therefore a live correctness
  suspect, not an unknown register interpretation.
- The historical slow `ps=0x5008F1400` loop walked a GPU-built linked list whose
  producer had previously been absent. Performance must be remeasured after the
  producer path is present before changing control-flow semantics.

## Next falsifiable checkpoint

Capture one paired producer boundary where a known-nonblack full-resolution
source feeds the first required half-resolution target:

1. record the source and destination guest descriptors, including tile mode,
   DCC address/lifetime, pitch, mip and format;
2. prove whether the writer draw/dispatch executes;
3. inspect the destination host image immediately after the writer;
4. inspect the same resource at its first sampled alias;
5. compare address/swizzle math with Sony's `libSceAgcGpuAddress.dll` and the
   metadata transition with the SDK compression samples.

This distinguishes the two remaining root-cause classes:

- the DCC decompress/fast-clear metadata operation is dropped or implemented
  incorrectly; or
- render-target residency/alias identity selects a fresh or incompatible host
  image when the written surface is sampled.

Do this offline from retained draw state where possible, then perform one
targeted boot only after the probe wiring and exact executable are verified.

## Ground-truth order

1. Sony Prospero SDK 10 headers, samples, NATVIS register definitions and host
   oracle DLLs under `ps5oracle/sony/prospero-sdk-10.00`.
2. Byte-exact firmware behavior and symbols under `ps5oracle/sony`.
3. LLVM's gfx1013 definition/MC tests and AMD's RDNA 1 ISA baseline for families
   missing from the partial Sony ISA capture.
4. PAL, Mesa ACO, KytyPS5, SharpEmu, Raeen and other curated implementations as
   differentials, never as authority over conflicting Sony evidence.

The partial Sony ISA capture has no instruction bodies for vector memory,
LDS/GDS, image, FLAT or export. A failed grep of that capture is not evidence of
absence. Prospero gfx1013 is GFX10.1 plus BVH ray-tracing instructions; it is not
a stock RDNA2 decode table.

## Avoided dead ends

- Do not re-litigate whether Astro renders any scene pixels; the full-resolution
  capture already answers that.
- Do not blame the final tone mapper without finding an earlier nonblack input.
- Do not use a draw count, trace share or absence of ERROR-level telemetry as a
  finding.
- Do not chase LLE ioctl `0xC0488131`; it is a strictly larger implementation
  surface than the HLE path.
- Do not use title-specific fabricated resource values to force progress.

## Relevant preserved material

- `docs/imported/sharpemu-home/astro-bot-boot.md`
- `docs/imported/sharpemu-home/astrobot-bringup.md`
- `docs/imported/sharpemu-home/astro-agc-conformance.md`
- `docs/imported/sharpemu-home/gpu-surface-gap.md`
- `docs/imported/sharpemu-home/methodology-execution.md`
- `docs/imported/sharpemu-home/what-the-firmware-taught-us.md`
- `ps5oracle/public-references/PUBLIC-ISA-REFERENCES-README.md`
- `ps5oracle/sony/prospero-sdk-10.00/HOST-TOOLS-README.md`

