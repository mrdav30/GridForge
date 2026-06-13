# Hex Prism Follow-Up Plan

**Status:** Active

**Goal:** Track topology work intentionally deferred from the first hex-prism
release so the completed implementation plan can stay closed without losing
future design context.

**Scope:** This plan is for follow-up validation and design work only. The first
hex-prism release already supports rectangular and hex grids in one
`GridWorld`, topology-aware lookup, tracing, blockers, occupants, scans,
benchmarks, and documentation.

## Deferred Work

### 1. Mixed-Topology Voxel Neighbor Bridging

Intent: decide whether rectangular-prism and hex-prism grids should ever expose
direct voxel-neighbor bridges across topology boundaries.

Questions to answer:

- What is the deterministic mapping from a rectangular boundary voxel to one or
  more hex boundary voxels?
- Should the bridge be one-to-one, one-to-many, or query-driven?
- How should caller APIs expose ambiguous cross-topology adjacency without
  reintroducing opaque direction slots?
- What benchmark shape proves the mapping is worth the added complexity?

Exit criteria:

- Either document mixed-topology neighbor bridging as permanently unsupported,
  or add a tested, benchmarked, and caller-friendly design.

### 2. Sparse Hex-Prism Validation

Status: completed on 2026-06-13.

Intent: validate the combined sparse-storage plus hex-topology path explicitly
before documenting it as a recommended workflow.

Checklist:

- [x] Add tests for sparse hex construction from topology-local `(q, layer, r)`
  configured indices.
- [x] Add lookup, coverage, blocker, occupant, scan, and runtime mutation coverage
  for sparse hex grids.
- [x] Evaluate whether benchmark coverage is needed beyond dense hex and sparse
  rectangular grid coverage.
- [x] Update sparse and topology docs if sparse hex becomes a highlighted scenario.

Result:

- Sparse hex grids are supported as configured topology-local axial cells.
- Focused tests cover construction, missing lookup, tracing, scan cells,
  blockers, occupant registration, radius scans, and runtime sparse mutation.
- No benchmark was added because validation did not expose a different
  performance shape from the existing sparse-storage and hex-topology benchmark
  surfaces.

Exit criteria:

- Sparse hex behavior is documented as supported with explicit coverage.
