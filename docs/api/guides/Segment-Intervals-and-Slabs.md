# Segment Intervals and Slabs

Use bounded exact intervals when an application must distinguish address-space
coverage from physical sparse presence. Supply caller-owned output and scratch,
then check the returned status before consuming the certificate.

## Exact Segment Intervals And Completed Slabs

`TraceIntervalsInto(...)` writes exact closed cell-prism intervals into a
caller-owned `SwiftList<GridTraceInterval>`, using reusable
`GridTraceIntervalScratch`. It includes missing sparse addresses, orders results
canonically, assigns simultaneous-coverage ties, and reports continuous address
and physical coverage separately. Grid, address, output, and combined candidate
work ceilings are independent. Address admission charges the original full
candidate box even when the rectangular collector omits planar misses.

## Completed rectangular slabs

`TryTraceRectangularSlabsInto(...)` adds a narrow synchronous observation path.
The caller supplies an exact `GridCoveredAddressGeneration`, an ordinal span at
least as long as `outputLimit`, and a `Func<GridTraceSlab, bool>` observer. It
supports exactly one active matching rectangular single-layer grid, nonzero
original X/Z deltas, and representable extreme prisms and slab arithmetic.
Unsupported input returns `false` before chargeable discovery, clears results,
and invokes no observer; ignore that report and use `TraceIntervalsInto(...)`.
Once qualified it returns `true` for every terminal outcome, including ceilings;
do not restart a failed qualified trace as an uncharged fallback.

Each packet identifies all newly appended intervals for one fully enumerated X
slab, in ascending X order—even when no interval exists. Missing physical
addresses still produce intervals. The original segment and admission box never
shrink. `SeparatesEndpoints` means only that the original endpoints lie strictly
beyond the actual opposing X faces with more than one raw parameter unit of
margin on each side. It does not assert an obstruction or navigation validity.

## Observer continuation and partial output

Return `true` to continue. Full completion uses the same canonical sort, ties,
and coverage as the eager tracer. The source ordinal at final position `i`
identifies that interval's original emission position, so a consumer can reorder
its own associated records without mapping them again. Return `false` to stop
immediately after the completed slab: the status is `StoppedAfterCompleteSlab`,
`IsComplete` is false, ties remain unassigned, and both coverage flags are
false. **Partial output is emission order, not a canonical ray prefix.**

## Locks, scratch, and failure cleanup

The observer runs under the world read lock. Do not reenter world/grid
operations or modify supplied results or scratch. Reading world identity and
change-stamp properties for freshness checks is allowed. The lock protects grid
lifetime, not concurrent obstacle updates; packets carry the original world
stamp captured before generation qualification, and consumers must perform their
own final freshness fence. Same-scratch tracing or `Clear()` during a callback
throws before clearing the active output. Scratch is not thread-safe. Normal
completion/stop releases temporary grid references. Failures and exceptions also
clear results and used ordinals; observer exceptions propagate.

## Related guides

- [GridTracer and Coverage](GridTracer-and-Coverage.md) for physical coverage
  and pooled result lifetimes.
- [Navigation Body Coverage](Navigation-Body-Coverage.md) for swept footprints.
- [World Changes and Composition](World-Changes-and-Composition.md) for revision
  fences.
