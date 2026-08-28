//=======================================================================
// GridCellGeometry.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using FixedMathSharp.Geometry;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using SwiftCollections;

namespace GridForge.Grids.Topology;

/// <summary>
/// Builds exact topology-owned cell prisms and contact manifolds without floating-point conversion.
/// </summary>
public static partial class GridCellGeometry
{
    private const int MaximumIntersectionCandidateCount = 48;

    /// <summary>
    /// Attempts to build the exact prism for one physical voxel in an active grid.
    /// </summary>
    public static bool TryGetPrism(
        VoxelGrid grid,
        VoxelIndex index,
        out GridCellPrism prism)
    {
        SwiftThrowHelper.ThrowIfNull(grid, nameof(grid));

        if (!grid.IsActive || !grid.TryGetVoxel(index, out Voxel? voxel))
        {
            prism = default;
            return false;
        }

        return TryCreatePrism(
            grid.Configuration.TopologyKind,
            grid.Configuration.TopologyMetrics,
            voxel!.WorldPosition,
            voxel.WorldIndex,
            out prism);
    }

    /// <summary>
    /// Attempts to build an exact prism from normalized topology metrics and a world-space cell center.
    /// </summary>
    /// <remarks>
    /// This overload permits offline geometry construction. The supplied identity is copied verbatim.
    /// </remarks>
    public static bool TryCreatePrism(
        GridTopologyKind topologyKind,
        GridTopologyMetrics topologyMetrics,
        Vector3d center,
        WorldVoxelIndex cell,
        out GridCellPrism prism)
    {
        if (!GridTopologyMetrics.IsValid(topologyKind, topologyMetrics))
        {
            prism = default;
            return false;
        }

        GridTopologyMetrics metrics = GridTopologyMetrics.Normalize(topologyKind, topologyMetrics);
        if (!TryGetExactHalf(metrics.LayerHeight, out Fixed64 halfHeight))
        {
            prism = default;
            return false;
        }

        if (!TryGetSymmetricBounds(center.Y, halfHeight, out Fixed64 verticalMin, out Fixed64 verticalMax))
        {
            prism = default;
            return false;
        }

        Span<Vector2d> footprint = stackalloc Vector2d[6];
        int vertexCount;
        Fixed64 planarInradius;

        if (topologyKind == GridTopologyKind.RectangularPrism)
        {
            if (!TryGetExactHalf(metrics.CellWidth, out Fixed64 halfWidth)
                || !TryGetExactHalf(metrics.CellLength, out Fixed64 halfLength))
            {
                prism = default;
                return false;
            }

            bool hasExactBounds = TryGetSymmetricBounds(
                center.X,
                halfWidth,
                out Fixed64 minX,
                out Fixed64 maxX);
            hasExactBounds &= TryGetSymmetricBounds(
                center.Z,
                halfLength,
                out Fixed64 minZ,
                out Fixed64 maxZ);
            if (!hasExactBounds)
            {
                prism = default;
                return false;
            }
            footprint[0] = new Vector2d(minX, minZ);
            footprint[1] = new Vector2d(maxX, minZ);
            footprint[2] = new Vector2d(maxX, maxZ);
            footprint[3] = new Vector2d(minX, maxZ);
            vertexCount = 4;
            planarInradius = FixedMath.Min(halfWidth, halfLength);
        }
        else
        {
            Fixed64 radius = metrics.CellRadius;
            if (!Fixed64.TryMultiplyAdd(
                    radius,
                    HexCoordinateUtility.Sqrt3,
                    Fixed64.Zero,
                    out Fixed64 fullWidth))
            {
                prism = default;
                return false;
            }

            if (!TryGetExactHalf(radius, out Fixed64 halfRadius)
                || !TryGetExactHalf(fullWidth, out Fixed64 apothem))
            {
                prism = default;
                return false;
            }
            if (metrics.HexOrientation == HexOrientation.FlatTop)
            {
                bool hasExactBounds = TryGetSymmetricBounds(
                    center.X,
                    radius,
                    out Fixed64 minRadiusX,
                    out Fixed64 maxRadiusX);
                hasExactBounds &= TryGetSymmetricBounds(
                    center.X,
                    halfRadius,
                    out Fixed64 minHalfX,
                    out Fixed64 maxHalfX);
                hasExactBounds &= TryGetSymmetricBounds(
                    center.Z,
                    apothem,
                    out Fixed64 minApothemZ,
                    out Fixed64 maxApothemZ);
                if (!hasExactBounds)
                {
                    prism = default;
                    return false;
                }

                footprint[0] = new Vector2d(minRadiusX, center.Z);
                footprint[1] = new Vector2d(minHalfX, minApothemZ);
                footprint[2] = new Vector2d(maxHalfX, minApothemZ);
                footprint[3] = new Vector2d(maxRadiusX, center.Z);
                footprint[4] = new Vector2d(maxHalfX, maxApothemZ);
                footprint[5] = new Vector2d(minHalfX, maxApothemZ);
            }
            else
            {
                bool hasExactBounds = TryGetSymmetricBounds(
                    center.Z,
                    radius,
                    out Fixed64 minRadiusZ,
                    out Fixed64 maxRadiusZ);
                hasExactBounds &= TryGetSymmetricBounds(
                    center.Z,
                    halfRadius,
                    out Fixed64 minHalfZ,
                    out Fixed64 maxHalfZ);
                hasExactBounds &= TryGetSymmetricBounds(
                    center.X,
                    apothem,
                    out Fixed64 minApothemX,
                    out Fixed64 maxApothemX);
                if (!hasExactBounds)
                {
                    prism = default;
                    return false;
                }

                footprint[0] = new Vector2d(center.X, minRadiusZ);
                footprint[1] = new Vector2d(maxApothemX, minHalfZ);
                footprint[2] = new Vector2d(maxApothemX, maxHalfZ);
                footprint[3] = new Vector2d(center.X, maxRadiusZ);
                footprint[4] = new Vector2d(minApothemX, maxHalfZ);
                footprint[5] = new Vector2d(minApothemX, minHalfZ);
            }

            vertexCount = 6;
            planarInradius = apothem;
        }

        ReadOnlySpan<Vector2d> resolvedFootprint = footprint[..vertexCount];
        prism = new GridCellPrism(
            cell,
            topologyKind,
            center,
            verticalMin,
            verticalMax,
            planarInradius,
            resolvedFootprint);
        return true;
    }

    /// <summary>
    /// Computes the exact closed-set contact between two cell prisms.
    /// </summary>
    public static VoxelContactManifold GetContact(
        in GridCellPrism source,
        in GridCellPrism target)
    {
        Vector3d sourceToTarget = target.Center - source.Center;
        Fixed64 verticalMin = FixedMath.Max(source.VerticalMin, target.VerticalMin);
        Fixed64 verticalMax = FixedMath.Min(source.VerticalMax, target.VerticalMax);
        if (verticalMin > verticalMax)
            return CreateSeparated(source.Cell, target.Cell, sourceToTarget);

        Span<Vector2d> intersection = stackalloc Vector2d[GridConvexPolygon2d.MaxVertexCount];
        int intersectionCount = BuildFootprintIntersection(source, target, intersection);
        if (intersectionCount == 0)
            return CreateSeparated(source.Cell, target.Cell, sourceToTarget);

        bool hasVerticalSpan = verticalMax > verticalMin;
        if (intersectionCount >= 3
            && FixedConvex2dRelations.IsStrictlyConvex(intersection[..intersectionCount])
            && FixedConvex2dRelations.TryGetAreaAndCentroid(
                intersection[..intersectionCount],
                out Fixed64 overlapArea,
                out _))
        {
            // A strictly convex hull proves positive exact area even when the
            // narrowed Fixed64 area underflows to zero.
            bool areaRepresentable = overlapArea > Fixed64.Zero
                && overlapArea != Fixed64.MaxValue;
            GridConvexPolygon2d polygon = new GridConvexPolygon2d(intersection[..intersectionCount]);
            return new VoxelContactManifold(
                source.Cell,
                target.Cell,
                sourceToTarget,
                hasVerticalSpan ? VoxelContactKind.VolumeOverlap : VoxelContactKind.Face,
                hasVerticalSpan ? VoxelContactFaceKind.None : VoxelContactFaceKind.Horizontal,
                verticalMin,
                verticalMax,
                default,
                default,
                polygon,
                overlapArea,
                areaRepresentable);
        }

        GetSegmentExtents(intersection[..intersectionCount], out Vector2d segmentStart, out Vector2d segmentEnd);
        bool hasHorizontalSpan = segmentStart != segmentEnd;
        if (hasHorizontalSpan && hasVerticalSpan)
        {
            Fixed64 width = Vector2d.Distance(segmentStart, segmentEnd);
            Fixed64 height = verticalMax - verticalMin;
            bool areaRepresentable = Fixed64.TryMultiplyAdd(
                width,
                height,
                Fixed64.Zero,
                out Fixed64 faceArea);
            return new VoxelContactManifold(
                source.Cell,
                target.Cell,
                sourceToTarget,
                VoxelContactKind.Face,
                VoxelContactFaceKind.Vertical,
                verticalMin,
                verticalMax,
                segmentStart,
                segmentEnd,
                default,
                faceArea,
                areaRepresentable);
        }

        VoxelContactKind kind = hasHorizontalSpan || hasVerticalSpan
            ? VoxelContactKind.Edge
            : VoxelContactKind.Point;
        return new VoxelContactManifold(
            source.Cell,
            target.Cell,
            sourceToTarget,
            kind,
            VoxelContactFaceKind.None,
            verticalMin,
            verticalMax,
            segmentStart,
            segmentEnd,
            default,
            Fixed64.Zero,
            true);
    }

    /// <summary>
    /// Attempts to get exact face geometry for a safe same-grid primary adjacency.
    /// </summary>
    /// <remarks>
    /// Rectangular diagonals and hex vertical-diagonal offsets are deliberately rejected.
    /// </remarks>
    public static bool TryGetPrimaryFace(
        VoxelGrid grid,
        VoxelIndex sourceIndex,
        VoxelIndex targetIndex,
        out VoxelContactManifold manifold)
    {
        if (grid == null)
            throw new ArgumentNullException(nameof(grid));

        manifold = default;
        if (!IsPrimaryOffset(grid.Configuration.TopologyKind, sourceIndex, targetIndex)
            || !TryGetPrism(grid, sourceIndex, out GridCellPrism source)
            || !TryGetPrism(grid, targetIndex, out GridCellPrism target))
        {
            return false;
        }

        manifold = GetContact(source, target);
        return manifold.Kind == VoxelContactKind.Face;
    }

    /// <summary>
    /// Builds exact contacts between physical voxels in a candidate grid pair into caller-owned output.
    /// </summary>
    /// <remarks>
    /// Source and target cells are processed in canonical local-index order. The supplied scratch retains
    /// broad-phase capacity so warmed calls allocate no managed memory. Separated AABB candidates are omitted.
    /// </remarks>
    /// <returns>The number of manifolds written to <paramref name="results"/>.</returns>
    public static int GetExactBoundaryContactsInto(
        VoxelGrid sourceGrid,
        VoxelGrid targetGrid,
        SwiftList<VoxelContactManifold> results,
        GridContactQueryScratch scratch)
    {
        SwiftThrowHelper.ThrowIfNull(sourceGrid, nameof(sourceGrid));
        SwiftThrowHelper.ThrowIfNull(targetGrid, nameof(targetGrid));
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));
        SwiftThrowHelper.ThrowIfNull(scratch, nameof(scratch));

        results.Clear();
        scratch.Clear();
        if (ReferenceEquals(sourceGrid, targetGrid)
            || !sourceGrid.IsActive
            || !targetGrid.IsActive
            || !ReferenceEquals(sourceGrid.World, targetGrid.World))
        {
            return 0;
        }

        try
        {
            CollectPotentialSourceVoxels(sourceGrid, targetGrid, scratch);

            for (int sourceIndex = 0; sourceIndex < scratch.SourceVoxels.Count; sourceIndex++)
            {
                Voxel sourceVoxel = scratch.SourceVoxels[sourceIndex];
                if (!TryGetPrism(sourceGrid, sourceVoxel.Index, out GridCellPrism sourcePrism))
                    continue;

                TopologyVoxelAabb sourceAabb = sourcePrism.GetAabb();
                TopologyVoxelAabb broadPhaseBounds = sourceAabb.Expand(targetGrid.Topology.MaxCellEdge);
                if (!TopologyVoxelRangeUtility.TryGetCandidateRange(
                    targetGrid,
                    broadPhaseBounds,
                    out VoxelIndex minIndex,
                    out VoxelIndex maxIndex))
                {
                    continue;
                }

                scratch.CandidateVoxels.Clear();
                scratch.ProcessedVoxels.Clear();
                targetGrid.AddVoxelsInIndexRange(
                    minIndex,
                    maxIndex,
                    scratch.CandidateVoxels,
                    scratch.ProcessedVoxels);
                SortByVoxelIndex(scratch.CandidateVoxels);

                for (int targetIndex = 0; targetIndex < scratch.CandidateVoxels.Count; targetIndex++)
                {
                    Voxel targetVoxel = scratch.CandidateVoxels[targetIndex];
                    if (!TryGetPrism(targetGrid, targetVoxel.Index, out GridCellPrism targetPrism)
                        || !sourceAabb.Overlaps(targetPrism.GetAabb(), Fixed64.Zero))
                    {
                        continue;
                    }

                    VoxelContactManifold manifold = GetContact(sourcePrism, targetPrism);
                    if (manifold.Kind != VoxelContactKind.Separated)
                        results.Add(manifold);
                }
            }

            return results.Count;
        }
        finally
        {
            scratch.Clear();
        }
    }

    private static int BuildFootprintIntersection(
        in GridCellPrism source,
        in GridCellPrism target,
        Span<Vector2d> intersection)
    {
        Span<Vector2d> sourceVertices = stackalloc Vector2d[6];
        Span<Vector2d> targetVertices = stackalloc Vector2d[6];
        source.CopyFootprintTo(sourceVertices);
        target.CopyFootprintTo(targetVertices);
        ReadOnlySpan<Vector2d> sourceFootprint = sourceVertices[..source.FootprintVertexCount];
        ReadOnlySpan<Vector2d> targetFootprint = targetVertices[..target.FootprintVertexCount];
        Span<Vector2d> sourceOffsets = stackalloc Vector2d[6];
        Span<Vector2d> targetOffsets = stackalloc Vector2d[6];
        Vector2d sourceOrigin = new Vector2d(source.Center.X, source.Center.Z);
        Vector2d targetOrigin = new Vector2d(target.Center.X, target.Center.Z);
        for (int i = 0; i < sourceFootprint.Length; i++)
            sourceOffsets[i] = sourceFootprint[i] - sourceOrigin;
        for (int i = 0; i < targetFootprint.Length; i++)
            targetOffsets[i] = targetFootprint[i] - targetOrigin;
        ReadOnlySpan<Vector2d> resolvedSourceOffsets = sourceOffsets[..sourceFootprint.Length];
        ReadOnlySpan<Vector2d> resolvedTargetOffsets = targetOffsets[..targetFootprint.Length];
        Span<Vector2d> candidates = stackalloc Vector2d[MaximumIntersectionCandidateCount];
        int candidateCount = 0;

        for (int i = 0; i < sourceFootprint.Length; i++)
        {
            Vector2d vertex = sourceFootprint[i];
            if (FixedConvex2dRelations.ContainsPoint(vertex, targetOrigin, resolvedTargetOffsets))
                AddUnique(candidates, ref candidateCount, vertex);
        }

        for (int i = 0; i < targetFootprint.Length; i++)
        {
            Vector2d vertex = targetFootprint[i];
            if (FixedConvex2dRelations.ContainsPoint(vertex, sourceOrigin, resolvedSourceOffsets))
                AddUnique(candidates, ref candidateCount, vertex);
        }

        for (int sourceEdge = 0; sourceEdge < sourceFootprint.Length; sourceEdge++)
        {
            FixedSegment2d sourceSegment = new FixedSegment2d(
                sourceFootprint[sourceEdge],
                sourceFootprint[(sourceEdge + 1) % sourceFootprint.Length]);
            for (int targetEdge = 0; targetEdge < targetFootprint.Length; targetEdge++)
            {
                FixedSegment2d targetSegment = new FixedSegment2d(
                    targetFootprint[targetEdge],
                    targetFootprint[(targetEdge + 1) % targetFootprint.Length]);
                if (sourceSegment.TryGetUniqueIntersection(targetSegment, out Fixed64 parameter))
                {
                    AddUnique(
                        candidates,
                        ref candidateCount,
                        Vector2d.Lerp(sourceSegment.Start, sourceSegment.End, parameter));
                }
            }
        }

        if (candidateCount == 0)
            return 0;
        if (candidateCount == 1)
        {
            intersection[0] = candidates[0];
            return 1;
        }

        return BuildConvexHull(candidates[..candidateCount], intersection);
    }

    private static int BuildConvexHull(Span<Vector2d> candidates, Span<Vector2d> destination)
    {
        for (int i = 1; i < candidates.Length; i++)
        {
            Vector2d candidate = candidates[i];
            int insertion = i - 1;
            while (insertion >= 0 && CompareCoordinates(candidates[insertion], candidate) > 0)
            {
                candidates[insertion + 1] = candidates[insertion];
                insertion--;
            }

            candidates[insertion + 1] = candidate;
        }

        Span<Vector2d> hull = stackalloc Vector2d[MaximumIntersectionCandidateCount * 2];
        int count = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            while (count >= 2
                && Vector2d.OrientationSign(hull[count - 2], hull[count - 1], candidates[i]) <= 0)
                count--;

            hull[count++] = candidates[i];
        }

        int lowerCount = count;
        for (int i = candidates.Length - 2; i >= 0; i--)
        {
            while (count > lowerCount
                && Vector2d.OrientationSign(hull[count - 2], hull[count - 1], candidates[i]) <= 0)
            {
                count--;
            }

            hull[count++] = candidates[i];
        }

        count--;
        hull[..count].CopyTo(destination);
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetExactHalf(Fixed64 value, out Fixed64 half)
    {
        if ((value.m_rawValue & 1L) != 0L)
        {
            half = default;
            return false;
        }

        half = Fixed64.FromRaw(value.m_rawValue >> 1);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetSymmetricBounds(
        Fixed64 center,
        Fixed64 extent,
        out Fixed64 minimum,
        out Fixed64 maximum)
    {
        maximum = default;
        return Fixed64.TrySubtract(center, extent, out minimum)
            && Fixed64.TryAdd(center, extent, out maximum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddUnique(Span<Vector2d> candidates, ref int count, Vector2d candidate)
    {
        for (int i = 0; i < count; i++)
        {
            if (candidates[i] == candidate)
                return;
        }

        candidates[count++] = candidate;
    }

    private static void GetSegmentExtents(
        ReadOnlySpan<Vector2d> points,
        out Vector2d start,
        out Vector2d end)
    {
        start = points[0];
        end = points[^1];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CompareCoordinates(Vector2d first, Vector2d second)
    {
        int xComparison = first.X.CompareTo(second.X);
        return xComparison != 0 ? xComparison : first.Y.CompareTo(second.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPrimaryOffset(
        GridTopologyKind topologyKind,
        VoxelIndex source,
        VoxelIndex target)
    {
        int x = target.x - source.x;
        int y = target.y - source.y;
        int z = target.z - source.z;
        if (topologyKind == GridTopologyKind.RectangularPrism)
        {
            RectangularDirection direction = RectangularDirectionUtility.GetDirectionFromOffset((x, y, z));
            return RectangularDirectionUtility.IsPerpendicularNeighbor(direction);
        }

        VoxelIndex offset = new(x, y, z);
        ReadOnlySpan<HexDirection> directions = HexDirectionUtility.Primary;
        for (int i = 0; i < directions.Length; i++)
        {
            if (HexDirectionUtility.GetOffset(directions[i]) == offset)
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static VoxelContactManifold CreateSeparated(
        WorldVoxelIndex source,
        WorldVoxelIndex target,
        Vector3d sourceToTarget) =>
        new VoxelContactManifold(
            source,
            target,
            sourceToTarget,
            VoxelContactKind.Separated,
            VoxelContactFaceKind.None,
            default,
            default,
            default,
            default,
            default,
            Fixed64.Zero,
            true);

    private static void SortByVoxelIndex(SwiftList<Voxel> voxels)
    {
        Voxel[] items = voxels.InnerArray;
        int count = voxels.Count;

        // Array.Sort allocates a comparer-backed sorting helper on this hot path.
        // Heap sort keeps ordering deterministic without allocating or degrading
        // to quadratic behavior for large grid boundaries.
        for (int root = (count >> 1) - 1; root >= 0; root--)
            SiftDown(items, root, count);

        for (int end = count - 1; end > 0; end--)
        {
            (items[0], items[end]) = (items[end], items[0]);
            SiftDown(items, 0, end);
        }
    }

    private static void SiftDown(Voxel[] items, int root, int count)
    {
        while (true)
        {
            int child = (root << 1) + 1;
            if (child >= count)
                return;

            int right = child + 1;
            if (right < count && items[child].Index.CompareTo(items[right].Index) < 0)
                child = right;

            if (items[root].Index.CompareTo(items[child].Index) >= 0)
                return;

            (items[root], items[child]) = (items[child], items[root]);
            root = child;
        }
    }

    private static void CollectPotentialSourceVoxels(
        VoxelGrid sourceGrid,
        VoxelGrid targetGrid,
        GridContactQueryScratch scratch)
    {
        if (targetGrid.Configuration.StorageKind == GridStorageKind.Sparse)
        {
            AllocatedVoxelCollector collector = new AllocatedVoxelCollector(scratch.CandidateVoxels);
            targetGrid.VisitVoxels(ref collector);
            for (int i = 0; i < scratch.CandidateVoxels.Count; i++)
            {
                Voxel targetVoxel = scratch.CandidateVoxels[i];
                if (!TryGetPrism(targetGrid, targetVoxel.Index, out GridCellPrism targetPrism))
                    continue;

                AddSourceCandidates(
                    sourceGrid,
                    targetPrism.GetAabb().Expand(sourceGrid.Topology.MaxCellEdge),
                    scratch);
            }
        }
        else
        {
            Fixed64 expansion = targetGrid.Topology.MaxCellEdge + sourceGrid.Topology.MaxCellEdge;
            AddSourceCandidates(
                sourceGrid,
                new TopologyVoxelAabb(targetGrid.BoundsMin, targetGrid.BoundsMax).Expand(expansion),
                scratch);
        }

        scratch.CandidateVoxels.Clear();
        scratch.ProcessedVoxels.Clear();
        SortByVoxelIndex(scratch.SourceVoxels);
    }

    private static void AddSourceCandidates(
        VoxelGrid sourceGrid,
        TopologyVoxelAabb targetBounds,
        GridContactQueryScratch scratch)
    {
        if (!TopologyVoxelRangeUtility.TryGetCandidateRange(
            sourceGrid,
            targetBounds,
            out VoxelIndex minIndex,
            out VoxelIndex maxIndex))
        {
            return;
        }

        sourceGrid.AddVoxelsInIndexRange(
            minIndex,
            maxIndex,
            scratch.SourceVoxels,
            scratch.ProcessedVoxels);
    }

    private readonly struct AllocatedVoxelCollector : IVoxelStorageVisitor
    {
        private readonly SwiftList<Voxel> _results;

        public AllocatedVoxelCollector(SwiftList<Voxel> results)
        {
            _results = results;
        }

        public bool Visit(Voxel voxel)
        {
            _results.Add(voxel);
            return true;
        }
    }
}
