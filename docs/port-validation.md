# Port validation policy

Prosperismo combines a current Kyty-derived native core with selected,
validated work from SharpEmu and other concrete projects. A donor implementation
is a lead, not ground truth.

For every proposed native port:

1. Identify one externally observable contract or exact machine-level rule.
2. Establish current Kyty behavior with a focused test or offline replay.
3. Establish the donor behavior with the same input.
4. Resolve differences using Sony SDK/firmware/runtime evidence first, then
   LLVM gfx1013 definitions/tests for uncovered ISA families.
5. Keep Kyty unchanged when it is already correct or more general.
6. Port the smallest general fix only when the differential demonstrates a
   defect, and retain the reproducer as a regression test.

Do not port:

- title-specific bypasses;
- fabricated constants or bindings;
- diagnostic environment-variable scaffolding as production behavior;
- an implementation merely because it advances one title further;
- assumptions inferred from absence in the incomplete Sony ISA capture.

Performance is part of the contract. A correctness port must be checked against
Kyty's existing working titles and must not replace an asynchronous/native path
with a slower CPU/readback path without measured justification.

Labels in investigation notes remain mandatory: **CONFIRMED**,
**DIFFERENTIAL**, **ASSUMED**, **RETRACTED**, or **DEAD END**.
