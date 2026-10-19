# Gateway conformance cases

`gateway-cases.json` is shared by every Voice SDK and copied here unchanged from the `conformance/`
directory next to the SDK repositories - never edit the expectations in one SDK only. That directory's
README describes the format and the canonical form the parsed events are compared in.

`ConformanceTests.cs` runs every case through `GatewayEventParser`. The cases use short ids (`g1`, `u1`,
...), while this SDK types ids as `Guid`, so the test swaps each id for a fixed GUID before parsing and
back when comparing. `Guid.Empty` counts as a missing id.
