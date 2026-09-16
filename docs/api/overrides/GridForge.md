---
uid: GridForge
summary: *content
---

GridForge provides deterministic rectangular and hex-prism voxel worlds,
storage-neutral grid queries, transient runtime identity, blockers, occupants,
partitions, traversal, and diagnostic geometry for engine-agnostic .NET
applications.

`GridTracer` provides bounded exact segment intervals with canonical ordering
and missing-address evidence, plus synchronous completed-X-slab observation for
qualified single-layer rectangular traces. Partial slab output is not a
canonical ray prefix; see the
[tracing guide](https://github.com/mrdav30/GridForge/wiki/GridTracer-and-Coverage)
for ceilings, source ordinals, and observer lifetime rules.

Unity projects should use the maintained
[GridForge-Unity packages](https://github.com/mrdav30/GridForge-Unity) for scene
authoring, inspectors, gizmos, logging, samples, and conversion at the engine
boundary.
