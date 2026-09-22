//=======================================================================
// GridTracer.PlanarNavigationBody.Geometry.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using FixedMathSharp.Geometry;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;

namespace GridForge.Utility;

/// <content>Owns planar-only footprint geometry and finite lattice-union closure.</content>
public static partial class GridTracer
{
    private static bool MatchesPlanarEndpoint(GridWorld world, VoxelGrid grid, int layer, WorldVoxelIndex endpoint) =>
        endpoint.WorldSpawnToken == world.SpawnToken && endpoint.GridIndex == grid.GridIndex
        && endpoint.GridSpawnToken == grid.SpawnToken && endpoint.VoxelIndex.y == layer
        && endpoint.VoxelIndex.x >= 0 && endpoint.VoxelIndex.x < grid.Width
        && endpoint.VoxelIndex.z >= 0 && endpoint.VoxelIndex.z < grid.Length;

    private static bool TryCreatePlanarBodyBounds(Vector2d start, Vector2d end, Fixed64 axis, Fixed64 radius,
        out Vector2d min, out Vector2d max)
    {
        min = default;
        max = default;
        // Only the broad phase rounds outward; the exact relation retains an odd half-axis.
        Fixed64 halfAxis = Fixed64.FromRaw((axis.m_rawValue >> 1) + (axis.m_rawValue & 1));
        if (!Fixed64.TryAdd(halfAxis, radius, out Fixed64 extent)
            || !Fixed64.TrySubtract(FixedMath.Min(start.X, end.X), radius, out Fixed64 minX)
            || !Fixed64.TryAdd(FixedMath.Max(start.X, end.X), radius, out Fixed64 maxX)
            || !Fixed64.TrySubtract(FixedMath.Min(start.Y, end.Y), extent, out Fixed64 minZ)
            || !Fixed64.TryAdd(FixedMath.Max(start.Y, end.Y), extent, out Fixed64 maxZ))
            return false;
        min = new Vector2d(minX, minZ);
        max = new Vector2d(maxX, maxZ);
        return true;
    }

    private static bool TryExpandPlanarCandidateBounds(VoxelGrid grid, Vector2d min, Vector2d max,
        out Vector2d candidateMin, out Vector2d candidateMax)
    {
        candidateMin = default;
        candidateMax = default;
        GridTopologyMetrics metrics = grid.Configuration.TopologyMetrics;
        Fixed64 x = grid.Configuration.TopologyKind == GridTopologyKind.HexPrism ? metrics.CellRadius : metrics.CellWidth * Fixed64.Half;
        Fixed64 z = grid.Configuration.TopologyKind == GridTopologyKind.HexPrism ? metrics.CellRadius : metrics.CellLength * Fixed64.Half;
        if (!Fixed64.TrySubtract(min.X, x, out Fixed64 minX) || !Fixed64.TryAdd(max.X, x, out Fixed64 maxX)
            || !Fixed64.TrySubtract(min.Y, z, out Fixed64 minZ) || !Fixed64.TryAdd(max.Y, z, out Fixed64 maxZ))
            return false;
        candidateMin = new Vector2d(minX, minZ);
        candidateMax = new Vector2d(maxX, maxZ);
        return true;
    }

    private static bool TryCreatePlanarProofPrism(VoxelGrid grid, Vector3d center, out GridCellPrism prism)
    {
        GridTopologyMetrics metrics = grid.Configuration.TopologyMetrics;
        // This is an internal footprint key, not a physical prism or a rotated world.
        // Removing Y and thickness lets the existing exact-alternative proof compare planar geometry.
        GridTopologyMetrics planar = new GridTopologyMetrics(metrics.CellRadius, metrics.CellWidth, Fixed64.One, metrics.CellLength, metrics.HexOrientation);
        return GridCellGeometry.TryCreatePrism(grid.Configuration.TopologyKind, planar,
            new Vector3d(center.X, Fixed64.Zero, center.Z), default, out prism);
    }

    private static bool HasClosedPlanarBodyContact(in GridCellPrism prism, Vector2d center, Fixed64 axis, Fixed64 radius)
    {
        Span<Vector2d> offsets = stackalloc Vector2d[6];
        Vector2d origin = prism.Center.ToVector2d();
        for (int i = 0; i < prism.FootprintVertexCount; i++)
            offsets[i] = prism.GetFootprintVertex(i) - origin;
        return FixedConvex2dRelations.IntersectsUprightCapsule(center, axis, radius,
            origin, offsets[..prism.FootprintVertexCount]);
    }

    private static bool HasStrictPlanarBodyOverlap(in GridCellPrism prism, Vector2d start, Vector2d end, Fixed64 axis, Fixed64 radius)
    {
        Span<Vector2d> offsets = stackalloc Vector2d[6];
        Vector2d origin = prism.Center.ToVector2d();
        for (int i = 0; i < prism.FootprintVertexCount; i++)
            offsets[i] = prism.GetFootprintVertex(i) - origin;
        return FixedConvex2dRelations.IntersectsSweptUprightCapsuleStrict(start, end, axis, radius, origin, offsets[..prism.FootprintVertexCount]);
    }

    private static int GetPlanarNavigationClosure(VoxelGrid grid, in GridCellPrism source, in GridCellPrism target, Span<GridCellPrism> closure)
    {
        closure[0] = source;
        if (AreSameNavigationBodyPrism(source, target))
            return 1;
        for (int slot = 0; slot < grid.Topology.NeighborSlotCount; slot++)
        {
            VoxelIndex offset = grid.Topology.GetNeighborOffset(slot);
            if (offset.y != 0)
                continue;
            if (!Vector3d.TryAdd(source.Center, grid.Topology.GetWorldOffset((offset.x, 0, offset.z)), out Vector3d center)
                || !TryCreatePlanarProofPrism(grid, center, out GridCellPrism neighbor))
                return 0;
            if (!AreSameNavigationBodyPrism(neighbor, target))
                continue;
            closure[1] = target;
            if (grid.Configuration.TopologyKind != GridTopologyKind.RectangularPrism || offset.x == 0 || offset.z == 0)
                return 2;
            // Each rectangular axis bound is already represented by source or target.
            // Their Cartesian combinations add no new arithmetic or topology dimension.
            _ = TryCreatePlanarProofPrism(grid, new Vector3d(source.Center.X, Fixed64.Zero, target.Center.Z), out closure[2]);
            _ = TryCreatePlanarProofPrism(grid, new Vector3d(target.Center.X, Fixed64.Zero, source.Center.Z), out closure[3]);
            return 4;
        }
        return 0;
    }

    private static GridNavigationBodyTraceStatus ClosePlanarBodyUnion(VoxelGrid sourceGrid, VoxelIndex source, VoxelGrid targetGrid, VoxelIndex target,
        Vector2d start, Vector2d end, Fixed64 axis, Fixed64 radius, GridNavigationBodyTraceScratch scratch)
    {
        SwiftList<GridNavigationBodyTraceCandidate> candidates = scratch.AddressCandidates;
        // Closed endpoint admission places both identities inside the outward-rounded
        // candidate range; closure retention keeps them even when contact is tangent.
        // Thus the shared identity lookups visit at most candidates.Count entries.
        int sourceCandidate = FindNavigationBodyCandidate(candidates, sourceGrid, source);
        int targetCandidate = FindNavigationBodyCandidate(candidates, targetGrid, target);
        bool pinBoth = targetCandidate != sourceCandidate
            && AreSameNavigationBodyPrism(candidates[sourceCandidate].Prism, candidates[targetCandidate].Prism);
        if (scratch.UnionMembers.Capacity < (pinBoth ? 2 : 1))
            return GridNavigationBodyTraceStatus.AddressLimitExceeded;
        AddNavigationBodyUnionMember(candidates, scratch.UnionMembers, sourceCandidate);
        if (pinBoth)
            AddNavigationBodyUnionMember(candidates, scratch.UnionMembers, targetCandidate);
        for (int i = 0; i < scratch.UnionMembers.Count; i++)
        {
            GridCellPrism member = candidates[scratch.UnionMembers[i]].Prism;
            for (int slot = 0; slot < sourceGrid.Topology.NeighborSlotCount; slot++)
            {
                VoxelIndex offset = sourceGrid.Topology.GetNeighborOffset(slot);
                if (offset.y != 0)
                    continue;
                if (!Vector3d.TryAdd(member.Center, sourceGrid.Topology.GetWorldOffset((offset.x, 0, offset.z)), out Vector3d center)
                    || !TryCreatePlanarProofPrism(sourceGrid, center, out GridCellPrism neighbor))
                    return GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry;
                int match = FindBestMatchingNavigationBodyPrism(candidates, neighbor, sourceCandidate, targetCandidate);
                bool overlap = match >= 0 ? candidates[match].HasPositiveOverlap : HasStrictPlanarBodyOverlap(neighbor, start, end, axis, radius);
                if (overlap && match < 0)
                    return GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry;
                if (match >= 0 && !candidates[match].IsVisited && !TryAddPlanarUnionMember(scratch, match))
                    return GridNavigationBodyTraceStatus.AddressLimitExceeded;
            }
        }
        // A distinct target is either pinned above or an admitted immediate source
        // neighbor, so the first closure iteration necessarily visits it.
        return GridNavigationBodyTraceStatus.Complete;
    }

    private static bool TryAddPlanarUnionMember(GridNavigationBodyTraceScratch scratch, int index)
    {
        if (scratch.UnionMembers.Count == scratch.UnionMembers.Capacity)
            return false;
        AddNavigationBodyUnionMember(scratch.AddressCandidates, scratch.UnionMembers, index);
        return true;
    }
}
