---
title: GridForge
description: API reference and guides for deterministic rectangular and hex-prism voxel worlds, spatial queries, blockers, occupants, and diagnostics.
---

<div class="gf-hero">
  <p class="gf-kicker">DETERMINISTIC VOXEL WORLDS FOR .NET</p>
  <h1>Build space your simulation can trust.</h1>
  <p>GridForge turns fixed-point world space into explicit, streamable voxel
  worlds with topology-aware lookup, coverage, blockers, occupants, and
  diagnostic geometry.</p>
  <div class="gf-actions">
    <a href="xref:GridForge">Browse the API</a>
    <a href="https://github.com/mrdav30/GridForge/wiki/Getting-Started">Get started</a>
  </div>
</div>

## Shape the world you need

<div class="gf-card-grid">
  <div class="gf-card">
    <h3><a href="xref:GridForge.Grids.GridWorld">Explicit worlds</a></h3>
    <p>Own one grid, many conjoined grids, or a changing set of streamed regions
    without process-global runtime state.</p>
  </div>
  <div class="gf-card">
    <h3><a href="xref:GridForge.Grids.Topology">Rectangular or hex</a></h3>
    <p>Choose deterministic rectangular-prism or hex-prism topology and metrics
    independently for each grid.</p>
  </div>
  <div class="gf-card">
    <h3><a href="xref:GridForge.Grids.Storage">Dense or sparse</a></h3>
    <p>Materialize every addressable cell or only the physical voxels your world
    actually contains.</p>
  </div>
</div>

## Query, mutate, and observe

<div class="gf-card-grid">
  <div class="gf-card">
    <h3><a href="xref:GridForge.Utility.GridTracer">Trace and cover</a></h3>
    <p>Resolve lines, boxes, and flat XZ areas into topology-aware voxel or
    scan-cell coverage across active grids.</p>
  </div>
  <div class="gf-card">
    <h3><a href="xref:GridForge.Blockers">Block and occupy</a></h3>
    <p>Stack world-space blockers, register dynamic occupants, scan nearby
    results, and attach typed voxel-local partitions.</p>
  </div>
  <div class="gf-card">
    <h3><a href="xref:GridForge.Diagnostics">Build adapters</a></h3>
    <p>Project physical cells, sparse holes, topology geometry, and dirty changes
    into engine tools without moving rendering into the core.</p>
  </div>
</div>

## Package family

| Package | Serialization profile |
| --- | --- |
| `GridForge` | Standard package with MemoryPack support |
| `GridForge.Lean` | Same grid APIs without the MemoryPack runtime dependency |

Unity projects can use the maintained
[GridForge-Unity packages](https://github.com/mrdav30/GridForge-Unity) for scene
authoring, inspectors, gizmos, logging, and samples.

## Resources

- [Human-readable wiki](https://github.com/mrdav30/GridForge/wiki)
- [Migration guide](https://github.com/mrdav30/GridForge/blob/main/docs/MIGRATION.md)
- [Source, issues, and releases](https://github.com/mrdav30/GridForge)
- [Unity packages and adapter API](https://mrdav30.github.io/GridForge-Unity/)
- [Core test-suite coverage](https://mrdav30.github.io/GridForge/coverage/)

The API reference is generated from the library XML documentation. The wiki
explains ownership, topology, storage, pooling, query lifetimes, and common
workflows in task-oriented prose.
