//=======================================================================
// GridScanManager.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using FixedMathSharp;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using SwiftCollections.Pool;

namespace GridForge.Grids;

/// <summary>
/// Provides efficient querying methods for retrieving occupants within a grid.
/// Handles spatial lookups for voxels, filtering by occupant type, and fetching occupants using unique tickets.
/// </summary>
public static class GridScanManager
{
    #region Scan Methods

    /// <summary>
    /// Scans for occupants within a given radius from a specified position in the supplied world.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="radius">The search radius in fixed-point world units. Squared-distance filtering includes the boundary.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <returns>A lazy sequence of matching occupant references. Null or inactive worlds yield no results. Consume it before mutating the world.</returns>
    /// <remarks>The radius is squared without a sign check: negative radii currently use their magnitude for distance filtering. Broad-phase bounds use the signed radius. Prefer nonnegative radii and representable coordinate/radius arithmetic. Results follow grid/scan-cell/bucket traversal order; no distance sort or global occupant deduplication is promised. One occupant registered in multiple voxels may appear more than once.Enumeration materializes matches into a temporary pooled list before the first yield, then releases that list when enumeration completes or is disposed. Predicate exceptions propagate after cleanup. Copy references if needed; do not retain pooled storage.
    /// </remarks>
    public static IEnumerable<IVoxelOccupant> ScanRadius(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        if (world == null || !world.IsActive)
            return Enumerable.Empty<IVoxelOccupant>();

        return ScanRadiusIterator(world, position, radius, occupantCondition, groupCondition);
    }

    /// <summary>
    /// Scans for occupants within a 2D XZ radius on the supplied world Y layer.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="position">The world X/Z position: vector X maps to world X, vector Y to world Z.</param>
    /// <param name="radius">The search radius in fixed-point world units. Squared-distance filtering includes the boundary.</param>
    /// <param name="layerY">The world Y position used to resolve the planar layer; defaults to zero.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <returns>A lazy sequence of matching occupant references. Null or inactive worlds yield no results. Consume it before mutating the world.</returns>
    /// <remarks>The radius is squared without a sign check: negative radii currently use their magnitude for distance filtering. Broad-phase bounds use the signed radius. Prefer nonnegative radii and representable coordinate/radius arithmetic. Results follow grid/scan-cell/bucket traversal order; no distance sort or global occupant deduplication is promised. One occupant registered in multiple voxels may appear more than once.Enumeration materializes matches into a temporary pooled list before the first yield, then releases that list when enumeration completes or is disposed. Predicate exceptions propagate after cleanup. Copy references if needed; do not retain pooled storage.
    /// </remarks>
    public static IEnumerable<IVoxelOccupant> ScanRadius(
        GridWorld world,
        Vector2d position,
        Fixed64 radius,
        Fixed64 layerY = default,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        if (world == null || !world.IsActive)
            return Enumerable.Empty<IVoxelOccupant>();

        return ScanRadius2dIterator(world, position, radius, layerY, occupantCondition, groupCondition);
    }

    /// <summary>
    /// Scans for occupants of a specific type within a given radius in the supplied world.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="radius">The search radius in fixed-point world units. Squared-distance filtering includes the boundary.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <typeparam name="T">The occupant type to include; assignable implementations are accepted.</typeparam>
    /// <returns>A lazy sequence of matching occupant references. Null or inactive worlds yield no results. Consume it before mutating the world.</returns>
    /// <remarks>The radius is squared without a sign check: negative radii currently use their magnitude for distance filtering. Broad-phase bounds use the signed radius. Prefer nonnegative radii and representable coordinate/radius arithmetic. Results follow grid/scan-cell/bucket traversal order; no distance sort or global occupant deduplication is promised. One occupant registered in multiple voxels may appear more than once.Enumeration materializes matches into a temporary pooled list before the first yield, then releases that list when enumeration completes or is disposed. Predicate exceptions propagate after cleanup. Copy references if needed; do not retain pooled storage.
    /// </remarks>
    public static IEnumerable<T> ScanRadius<T>(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null) where T : IVoxelOccupant
    {
        return ScanRadius(world, position, radius, occupantCondition, groupCondition).OfType<T>();
    }

    /// <summary>
    /// Scans for occupants of a specific type within a 2D XZ radius on the supplied world Y layer.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="position">The world X/Z position: vector X maps to world X, vector Y to world Z.</param>
    /// <param name="radius">The search radius in fixed-point world units. Squared-distance filtering includes the boundary.</param>
    /// <param name="layerY">The world Y position used to resolve the planar layer; defaults to zero.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <typeparam name="T">The occupant type to include; assignable implementations are accepted.</typeparam>
    /// <returns>A lazy sequence of matching occupant references. Null or inactive worlds yield no results. Consume it before mutating the world.</returns>
    /// <remarks>The radius is squared without a sign check: negative radii currently use their magnitude for distance filtering. Broad-phase bounds use the signed radius. Prefer nonnegative radii and representable coordinate/radius arithmetic. Results follow grid/scan-cell/bucket traversal order; no distance sort or global occupant deduplication is promised. One occupant registered in multiple voxels may appear more than once.Enumeration materializes matches into a temporary pooled list before the first yield, then releases that list when enumeration completes or is disposed. Predicate exceptions propagate after cleanup. Copy references if needed; do not retain pooled storage.
    /// </remarks>
    public static IEnumerable<T> ScanRadius<T>(
        GridWorld world,
        Vector2d position,
        Fixed64 radius,
        Fixed64 layerY = default,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null) where T : IVoxelOccupant
    {
        return ScanRadius(world, position, radius, layerY, occupantCondition, groupCondition).OfType<T>();
    }

    /// <summary>
    /// Clears and fills caller-owned storage with occupants within the supplied radius.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="radius">The search radius in fixed-point world units. Squared-distance filtering includes the boundary.</param>
    /// <param name="results">Caller-owned output storage. The call clears existing elements before filling it; the collection is not retained.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="results"/> is null.</exception>
    /// <remarks>The radius is squared without a sign check: negative radii currently use their magnitude for distance filtering. Broad-phase bounds use the signed radius. Prefer nonnegative radii and representable coordinate/radius arithmetic. Results follow grid/scan-cell/bucket traversal order; no distance sort or global occupant deduplication is promised. One occupant registered in multiple voxels may appear more than once.Predicate exceptions propagate. Matches already appended remain in caller storage; the operation does not roll them back.
    /// </remarks>
    public static void ScanRadiusInto(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        SwiftList<IVoxelOccupant> results,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        ChronicleGuard.ThrowIfNull(results, nameof(results));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        AddRadiusOccupantsTo(world, position, radius, results, occupantCondition, groupCondition);
    }

    /// <summary>
    /// Clears and fills caller-owned storage with occupants within a 2D XZ radius on the supplied world Y layer.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="position">The world X/Z position: vector X maps to world X, vector Y to world Z.</param>
    /// <param name="radius">The search radius in fixed-point world units. Squared-distance filtering includes the boundary.</param>
    /// <param name="results">Caller-owned output storage. The call clears existing elements before filling it; the collection is not retained.</param>
    /// <param name="layerY">The world Y position used to resolve the planar layer; defaults to zero.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="results"/> is null.</exception>
    /// <remarks>The radius is squared without a sign check: negative radii currently use their magnitude for distance filtering. Broad-phase bounds use the signed radius. Prefer nonnegative radii and representable coordinate/radius arithmetic. Results follow grid/scan-cell/bucket traversal order; no distance sort or global occupant deduplication is promised. One occupant registered in multiple voxels may appear more than once.Predicate exceptions propagate. Matches already appended remain in caller storage; the operation does not roll them back.
    /// </remarks>
    public static void ScanRadiusInto(
        GridWorld world,
        Vector2d position,
        Fixed64 radius,
        SwiftList<IVoxelOccupant> results,
        Fixed64 layerY = default,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        ChronicleGuard.ThrowIfNull(results, nameof(results));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        AddRadiusOccupants2dTo(world, position, radius, layerY, results, occupantCondition, groupCondition);
    }

    /// <summary>
    /// Clears and fills caller-owned storage using caller-owned scratch collections.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="radius">The search radius in fixed-point world units. Squared-distance filtering includes the boundary.</param>
    /// <param name="results">Caller-owned output storage. The call clears existing elements before filling it; the collection is not retained.</param>
    /// <param name="scratch">Reusable caller-owned scratch. Contents are cleared during the call; capacity is retained. Do not share it between concurrent or reentrant calls.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="results"/>, <paramref name="scratch"/> is null.</exception>
    /// <remarks>The radius is squared without a sign check: negative radii currently use their magnitude for distance filtering. Broad-phase bounds use the signed radius. Prefer nonnegative radii and representable coordinate/radius arithmetic. Results follow grid/scan-cell/bucket traversal order; no distance sort or global occupant deduplication is promised. One occupant registered in multiple voxels may appear more than once.Predicate exceptions propagate. Matches already appended remain in caller storage; the operation does not roll them back.
    /// </remarks>
    public static void ScanRadiusInto(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        SwiftList<IVoxelOccupant> results,
        GridScanScratch scratch,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        ChronicleGuard.ThrowIfNull(results, nameof(results));
        ChronicleGuard.ThrowIfNull(scratch, nameof(scratch));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        AddRadiusOccupantsTo(world, position, radius, results, scratch, occupantCondition, groupCondition);
    }

    /// <summary>
    /// Clears and fills caller-owned storage using caller-owned scratch collections for a 2D XZ radius.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="position">The world X/Z position: vector X maps to world X, vector Y to world Z.</param>
    /// <param name="radius">The search radius in fixed-point world units. Squared-distance filtering includes the boundary.</param>
    /// <param name="results">Caller-owned output storage. The call clears existing elements before filling it; the collection is not retained.</param>
    /// <param name="scratch">Reusable caller-owned scratch. Contents are cleared during the call; capacity is retained. Do not share it between concurrent or reentrant calls.</param>
    /// <param name="layerY">The world Y position used to resolve the planar layer; defaults to zero.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="results"/>, <paramref name="scratch"/> is null.</exception>
    /// <remarks>The radius is squared without a sign check: negative radii currently use their magnitude for distance filtering. Broad-phase bounds use the signed radius. Prefer nonnegative radii and representable coordinate/radius arithmetic. Results follow grid/scan-cell/bucket traversal order; no distance sort or global occupant deduplication is promised. One occupant registered in multiple voxels may appear more than once.Predicate exceptions propagate. Matches already appended remain in caller storage; the operation does not roll them back.
    /// </remarks>
    public static void ScanRadiusInto(
        GridWorld world,
        Vector2d position,
        Fixed64 radius,
        SwiftList<IVoxelOccupant> results,
        GridScanScratch scratch,
        Fixed64 layerY = default,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        ChronicleGuard.ThrowIfNull(results, nameof(results));
        ChronicleGuard.ThrowIfNull(scratch, nameof(scratch));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        AddRadiusOccupants2dTo(world, position, radius, layerY, results, scratch, occupantCondition, groupCondition);
    }

    /// <summary>
    /// Clears and fills caller-owned storage with typed occupants within the supplied radius.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="radius">The search radius in fixed-point world units. Squared-distance filtering includes the boundary.</param>
    /// <param name="results">Caller-owned output storage. The call clears existing elements before filling it; the collection is not retained.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <typeparam name="T">The occupant type to include; assignable implementations are accepted.</typeparam>
    /// <exception cref="System.ArgumentNullException"><paramref name="results"/> is null.</exception>
    /// <remarks>The radius is squared without a sign check: negative radii currently use their magnitude for distance filtering. Broad-phase bounds use the signed radius. Prefer nonnegative radii and representable coordinate/radius arithmetic. Results follow grid/scan-cell/bucket traversal order; no distance sort or global occupant deduplication is promised. One occupant registered in multiple voxels may appear more than once.Predicate exceptions propagate. Matches already appended remain in caller storage; the operation does not roll them back.
    /// </remarks>
    public static void ScanRadiusInto<T>(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        SwiftList<T> results,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null) where T : IVoxelOccupant
    {
        ChronicleGuard.ThrowIfNull(results, nameof(results));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        AddRadiusOccupantsTo(world, position, radius, results, occupantCondition, groupCondition);
    }

    /// <summary>
    /// Clears and fills caller-owned typed storage with occupants within a 2D XZ radius on the supplied world Y layer.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="position">The world X/Z position: vector X maps to world X, vector Y to world Z.</param>
    /// <param name="radius">The search radius in fixed-point world units. Squared-distance filtering includes the boundary.</param>
    /// <param name="results">Caller-owned output storage. The call clears existing elements before filling it; the collection is not retained.</param>
    /// <param name="layerY">The world Y position used to resolve the planar layer; defaults to zero.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <typeparam name="T">The occupant type to include; assignable implementations are accepted.</typeparam>
    /// <exception cref="System.ArgumentNullException"><paramref name="results"/> is null.</exception>
    /// <remarks>The radius is squared without a sign check: negative radii currently use their magnitude for distance filtering. Broad-phase bounds use the signed radius. Prefer nonnegative radii and representable coordinate/radius arithmetic. Results follow grid/scan-cell/bucket traversal order; no distance sort or global occupant deduplication is promised. One occupant registered in multiple voxels may appear more than once.Predicate exceptions propagate. Matches already appended remain in caller storage; the operation does not roll them back.
    /// </remarks>
    public static void ScanRadiusInto<T>(
        GridWorld world,
        Vector2d position,
        Fixed64 radius,
        SwiftList<T> results,
        Fixed64 layerY = default,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null) where T : IVoxelOccupant
    {
        ChronicleGuard.ThrowIfNull(results, nameof(results));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        AddRadiusOccupants2dTo(world, position, radius, layerY, results, occupantCondition, groupCondition);
    }

    /// <summary>
    /// Clears and fills caller-owned typed storage using caller-owned scratch collections.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="radius">The search radius in fixed-point world units. Squared-distance filtering includes the boundary.</param>
    /// <param name="results">Caller-owned output storage. The call clears existing elements before filling it; the collection is not retained.</param>
    /// <param name="scratch">Reusable caller-owned scratch. Contents are cleared during the call; capacity is retained. Do not share it between concurrent or reentrant calls.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <typeparam name="T">The occupant type to include; assignable implementations are accepted.</typeparam>
    /// <exception cref="System.ArgumentNullException"><paramref name="results"/>, <paramref name="scratch"/> is null.</exception>
    /// <remarks>The radius is squared without a sign check: negative radii currently use their magnitude for distance filtering. Broad-phase bounds use the signed radius. Prefer nonnegative radii and representable coordinate/radius arithmetic. Results follow grid/scan-cell/bucket traversal order; no distance sort or global occupant deduplication is promised. One occupant registered in multiple voxels may appear more than once.Predicate exceptions propagate. Matches already appended remain in caller storage; the operation does not roll them back.
    /// </remarks>
    public static void ScanRadiusInto<T>(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        SwiftList<T> results,
        GridScanScratch scratch,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null) where T : IVoxelOccupant
    {
        ChronicleGuard.ThrowIfNull(results, nameof(results));
        ChronicleGuard.ThrowIfNull(scratch, nameof(scratch));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        AddRadiusOccupantsTo(world, position, radius, results, scratch, occupantCondition, groupCondition);
    }

    /// <summary>
    /// Clears and fills caller-owned typed storage using caller-owned scratch collections for a 2D XZ radius.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="position">The world X/Z position: vector X maps to world X, vector Y to world Z.</param>
    /// <param name="radius">The search radius in fixed-point world units. Squared-distance filtering includes the boundary.</param>
    /// <param name="results">Caller-owned output storage. The call clears existing elements before filling it; the collection is not retained.</param>
    /// <param name="scratch">Reusable caller-owned scratch. Contents are cleared during the call; capacity is retained. Do not share it between concurrent or reentrant calls.</param>
    /// <param name="layerY">The world Y position used to resolve the planar layer; defaults to zero.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <typeparam name="T">The occupant type to include; assignable implementations are accepted.</typeparam>
    /// <exception cref="System.ArgumentNullException"><paramref name="results"/>, <paramref name="scratch"/> is null.</exception>
    /// <remarks>The radius is squared without a sign check: negative radii currently use their magnitude for distance filtering. Broad-phase bounds use the signed radius. Prefer nonnegative radii and representable coordinate/radius arithmetic. Results follow grid/scan-cell/bucket traversal order; no distance sort or global occupant deduplication is promised. One occupant registered in multiple voxels may appear more than once.Predicate exceptions propagate. Matches already appended remain in caller storage; the operation does not roll them back.
    /// </remarks>
    public static void ScanRadiusInto<T>(
        GridWorld world,
        Vector2d position,
        Fixed64 radius,
        SwiftList<T> results,
        GridScanScratch scratch,
        Fixed64 layerY = default,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null) where T : IVoxelOccupant
    {
        ChronicleGuard.ThrowIfNull(results, nameof(results));
        ChronicleGuard.ThrowIfNull(scratch, nameof(scratch));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        AddRadiusOccupants2dTo(world, position, radius, layerY, results, scratch, occupantCondition, groupCondition);
    }

    #endregion

    #region Occupant Registration & Retrieval

    /// <summary>Enumerates matching occupant types registered to the requested voxel.</summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="index">The exact world/grid generation and topology-local voxel identity to resolve.</param>
    /// <typeparam name="T">The occupant type to include; assignable implementations are accepted.</typeparam>
    /// <returns>A lazy sequence of matching occupant references registered to this exact voxel. An unresolved or unoccupied voxel yields no results.</returns>
    /// <remarks>The registration bucket is selected when called, independently of the occupant's current Position. Entry enumeration and any filters are lazy and follow bucket slot order. Finish enumeration before any occupancy mutation, grid reset, or pooled reuse, including before the first enumeration; the result is a borrowed view, not a snapshot.</remarks>
    public static IEnumerable<T> GetVoxelOccupantsByType<T>(GridWorld world, WorldVoxelIndex index) where T : IVoxelOccupant
    {
        return world != null && world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            ? grid!.GetVoxelOccupantsByType<T>(voxel!)
            : Enumerable.Empty<T>();
    }

    /// <summary>Enumerates matching occupant types registered to the requested voxel.</summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <typeparam name="T">The occupant type to include; assignable implementations are accepted.</typeparam>
    /// <returns>A lazy sequence of matching occupant references registered to this exact voxel. An unresolved or unoccupied voxel yields no results.</returns>
    /// <remarks>The registration bucket is selected when called, independently of the occupant's current Position. Entry enumeration and any filters are lazy and follow bucket slot order. Finish enumeration before any occupancy mutation, grid reset, or pooled reuse, including before the first enumeration; the result is a borrowed view, not a snapshot.</remarks>
    public static IEnumerable<T> GetVoxelOccupantsByType<T>(this VoxelGrid grid, Vector3d position) where T : IVoxelOccupant
    {
        return grid.TryGetVoxel(position, out Voxel? voxel)
            ? grid.GetVoxelOccupantsByType<T>(voxel!)
            : Enumerable.Empty<T>();
    }

    /// <summary>Enumerates matching occupant types registered to the requested voxel.</summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="index">The topology-local voxel address.</param>
    /// <typeparam name="T">The occupant type to include; assignable implementations are accepted.</typeparam>
    /// <returns>A lazy sequence of matching occupant references registered to this exact voxel. An unresolved or unoccupied voxel yields no results.</returns>
    /// <remarks>The registration bucket is selected when called, independently of the occupant's current Position. Entry enumeration and any filters are lazy and follow bucket slot order. Finish enumeration before any occupancy mutation, grid reset, or pooled reuse, including before the first enumeration; the result is a borrowed view, not a snapshot.</remarks>
    public static IEnumerable<T> GetVoxelOccupantsByType<T>(this VoxelGrid grid, VoxelIndex index) where T : IVoxelOccupant
    {
        return grid.TryGetVoxel(index, out Voxel? voxel)
            ? grid.GetVoxelOccupantsByType<T>(voxel!)
            : Enumerable.Empty<T>();
    }

    /// <summary>Enumerates matching occupant types registered to the requested voxel.</summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="voxel">The physical voxel to inspect. The reference is borrowed and must not cross grid removal or pooled reuse.</param>
    /// <typeparam name="T">The occupant type to include; assignable implementations are accepted.</typeparam>
    /// <returns>A lazy sequence of matching occupant references registered to this exact voxel. An unresolved or unoccupied voxel yields no results.</returns>
    /// <remarks>The registration bucket is selected when called, independently of the occupant's current Position. Entry enumeration and any filters are lazy and follow bucket slot order. Finish enumeration before any occupancy mutation, grid reset, or pooled reuse, including before the first enumeration; the result is a borrowed view, not a snapshot.</remarks>
    public static IEnumerable<T> GetVoxelOccupantsByType<T>(this VoxelGrid grid, Voxel voxel) where T : IVoxelOccupant
    {
        return voxel == null
            ? Enumerable.Empty<T>()
            : grid.GetOccupants(voxel).OfType<T>();
    }

    /// <summary>
    /// Retrieves a specific occupant at a given world-scoped voxel identity using an occupant ticket in the supplied world.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="index">The exact world/grid generation and topology-local voxel identity to resolve.</param>
    /// <param name="ticket">The exact occupant registration ticket, including its generation.</param>
    /// <param name="occupant">The resolved occupant on success; default on failure. Use it only when the operation succeeds.</param>
    /// <returns>True when the exact ticket resolves in the requested voxel; otherwise false and the occupant output is null.</returns>
    public static bool TryGetVoxelOccupant(
        GridWorld world,
        WorldVoxelIndex index,
        OccupantTicket ticket,
        out IVoxelOccupant? occupant)
    {
        occupant = null;
        return world != null
            && world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            && grid!.TryGetVoxelOccupant(voxel!, ticket, out occupant);
    }

    /// <summary>
    /// Retrieves a specific occupant at a given world position using an occupant ticket.
    /// </summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="ticket">The exact occupant registration ticket, including its generation.</param>
    /// <param name="occupant">The resolved occupant on success; default on failure. Use it only when the operation succeeds.</param>
    /// <returns>True when the exact ticket resolves in the requested voxel; otherwise false and the occupant output is null.</returns>
    public static bool TryGetVoxelOccupant(
        this VoxelGrid grid,
        Vector3d position,
        OccupantTicket ticket,
        out IVoxelOccupant? occupant)
    {
        occupant = null;
        return grid.TryGetVoxel(position, out Voxel? voxel)
            && grid.TryGetVoxelOccupant(voxel!, ticket, out occupant);
    }

    /// <summary>
    /// Retrieves a specific occupant at a given voxel coordinate using an occupant ticket.
    /// </summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="index">The topology-local voxel address.</param>
    /// <param name="ticket">The exact occupant registration ticket, including its generation.</param>
    /// <param name="occupant">The resolved occupant on success; default on failure. Use it only when the operation succeeds.</param>
    /// <returns>True when the exact ticket resolves in the requested voxel; otherwise false and the occupant output is null.</returns>
    public static bool TryGetVoxelOccupant(
        this VoxelGrid grid,
        VoxelIndex index,
        OccupantTicket ticket,
        out IVoxelOccupant? occupant)
    {
        occupant = null;
        return grid.TryGetVoxel(index, out Voxel? voxel)
            && grid.TryGetVoxelOccupant(voxel!, ticket, out occupant);
    }

    /// <summary>
    /// Retrieves a specific occupant from a given voxel using an occupant ticket.
    /// </summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="voxel">The physical voxel to inspect. The reference is borrowed and must not cross grid removal or pooled reuse.</param>
    /// <param name="ticket">The exact occupant registration ticket, including its generation.</param>
    /// <param name="occupant">The resolved occupant on success; default on failure. Use it only when the operation succeeds.</param>
    /// <returns>True when the exact ticket resolves in the requested voxel; otherwise false and the occupant output is null.</returns>
    public static bool TryGetVoxelOccupant(
        this VoxelGrid grid,
        Voxel voxel,
        OccupantTicket ticket,
        out IVoxelOccupant? occupant)
    {
        occupant = null;
        return voxel.IsOccupied
            && grid.TryGetScanCell(voxel.ScanCellKey, out ScanCell? scanCell)
            && scanCell!.IsOccupied
            && scanCell.TryGetOccupantAt(voxel.WorldIndex, ticket, out occupant);
    }

    /// <summary>Enumerates occupants registered to the requested voxel.</summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="index">The exact world/grid generation and topology-local voxel identity to resolve.</param>
    /// <returns>A lazy sequence of occupant references registered to this exact voxel. An unresolved or unoccupied voxel yields no results.</returns>
    /// <remarks>The registration bucket is selected when called, independently of the occupant's current Position. Entry enumeration and any filters are lazy and follow bucket slot order. Finish enumeration before any occupancy mutation, grid reset, or pooled reuse, including before the first enumeration; the result is a borrowed view, not a snapshot.</remarks>
    public static IEnumerable<IVoxelOccupant> GetOccupants(GridWorld world, WorldVoxelIndex index)
    {
        if (world == null)
            return Enumerable.Empty<IVoxelOccupant>();

        if (!world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel))
            return Enumerable.Empty<IVoxelOccupant>();

        return grid!.GetOccupants(voxel!);
    }

    /// <summary>Enumerates occupants registered to the requested voxel.</summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <returns>A lazy sequence of occupant references registered to this exact voxel. An unresolved or unoccupied voxel yields no results.</returns>
    /// <remarks>The registration bucket is selected when called, independently of the occupant's current Position. Entry enumeration and any filters are lazy and follow bucket slot order. Finish enumeration before any occupancy mutation, grid reset, or pooled reuse, including before the first enumeration; the result is a borrowed view, not a snapshot.</remarks>
    public static IEnumerable<IVoxelOccupant> GetOccupants(this VoxelGrid grid, Vector3d position)
    {
        return grid.TryGetVoxel(position, out Voxel? targetVoxel)
            ? grid.GetOccupants(targetVoxel!)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    /// <summary>Enumerates occupants registered to the requested voxel.</summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="index">The topology-local voxel address.</param>
    /// <returns>A lazy sequence of occupant references registered to this exact voxel. An unresolved or unoccupied voxel yields no results.</returns>
    /// <remarks>The registration bucket is selected when called, independently of the occupant's current Position. Entry enumeration and any filters are lazy and follow bucket slot order. Finish enumeration before any occupancy mutation, grid reset, or pooled reuse, including before the first enumeration; the result is a borrowed view, not a snapshot.</remarks>
    public static IEnumerable<IVoxelOccupant> GetOccupants(this VoxelGrid grid, VoxelIndex index)
    {
        return grid.TryGetVoxel(index, out Voxel? targetVoxel)
            ? grid.GetOccupants(targetVoxel!)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    /// <summary>Enumerates occupants registered to the requested voxel.</summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="voxel">The physical voxel to inspect. The reference is borrowed and must not cross grid removal or pooled reuse.</param>
    /// <returns>A lazy sequence of occupant references registered to this exact voxel. An unresolved or unoccupied voxel yields no results.</returns>
    /// <remarks>The registration bucket is selected when called, independently of the occupant's current Position. Entry enumeration and any filters are lazy and follow bucket slot order. Finish enumeration before any occupancy mutation, grid reset, or pooled reuse, including before the first enumeration; the result is a borrowed view, not a snapshot.</remarks>
    public static IEnumerable<IVoxelOccupant> GetOccupants(this VoxelGrid grid, Voxel voxel)
    {
        if (!voxel.IsOccupied)
            return Enumerable.Empty<IVoxelOccupant>();

        if (!grid.TryGetScanCell(voxel.ScanCellKey, out ScanCell? scanCell))
            return Enumerable.Empty<IVoxelOccupant>();

        return scanCell!.IsOccupied
            ? scanCell.GetOccupantsFor(voxel.WorldIndex)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    /// <summary>Enumerates occupants registered to the requested voxel that pass the supplied filters.</summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="index">The exact world/grid generation and topology-local voxel identity to resolve.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <returns>A lazy sequence of matching occupant references registered to this exact voxel. An unresolved or unoccupied voxel yields no results.</returns>
    /// <remarks>The registration bucket is selected when called, independently of the occupant's current Position. Entry enumeration and any filters are lazy and follow bucket slot order. Finish enumeration before any occupancy mutation, grid reset, or pooled reuse, including before the first enumeration; the result is a borrowed view, not a snapshot.</remarks>
    public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
        GridWorld world,
        WorldVoxelIndex index,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        if (world == null)
            return Enumerable.Empty<IVoxelOccupant>();

        if (!world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel))
            return Enumerable.Empty<IVoxelOccupant>();

        return grid!.GetConditionalOccupants(voxel!, occupantCondition, groupCondition);
    }

    /// <summary>Enumerates occupants registered to the requested voxel that pass the supplied filters.</summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <returns>A lazy sequence of matching occupant references registered to this exact voxel. An unresolved or unoccupied voxel yields no results.</returns>
    /// <remarks>The registration bucket is selected when called, independently of the occupant's current Position. Entry enumeration and any filters are lazy and follow bucket slot order. Finish enumeration before any occupancy mutation, grid reset, or pooled reuse, including before the first enumeration; the result is a borrowed view, not a snapshot.</remarks>
    public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
        this VoxelGrid grid,
        Vector3d position,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        return grid.TryGetVoxel(position, out Voxel? voxel)
            ? grid.GetConditionalOccupants(voxel!, occupantCondition, groupCondition)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    /// <summary>Enumerates occupants registered to the requested voxel that pass the supplied filters.</summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="index">The topology-local voxel address.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <returns>A lazy sequence of matching occupant references registered to this exact voxel. An unresolved or unoccupied voxel yields no results.</returns>
    /// <remarks>The registration bucket is selected when called, independently of the occupant's current Position. Entry enumeration and any filters are lazy and follow bucket slot order. Finish enumeration before any occupancy mutation, grid reset, or pooled reuse, including before the first enumeration; the result is a borrowed view, not a snapshot.</remarks>
    public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
        this VoxelGrid grid,
        VoxelIndex index,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        return grid.TryGetVoxel(index, out Voxel? voxel)
            ? grid.GetConditionalOccupants(voxel!, occupantCondition, groupCondition)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    /// <summary>Enumerates occupants registered to the requested voxel that pass the supplied filters.</summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="targetVoxel">A current physical voxel belonging to the supplied grid.</param>
    /// <param name="occupantCondition">Optional occupant predicate. Null accepts every occupant that passes the other filters.</param>
    /// <param name="groupCondition">Optional group-ID predicate. Null accepts every group.</param>
    /// <returns>A lazy sequence of matching occupant references registered to this exact voxel. An unresolved or unoccupied voxel yields no results.</returns>
    /// <remarks>The registration bucket is selected when called, independently of the occupant's current Position. Entry enumeration and any filters are lazy and follow bucket slot order. Finish enumeration before any occupancy mutation, grid reset, or pooled reuse, including before the first enumeration; the result is a borrowed view, not a snapshot.</remarks>
    public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
        this VoxelGrid grid,
        Voxel targetVoxel,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        if (targetVoxel == null || !targetVoxel.IsOccupied)
            return Enumerable.Empty<IVoxelOccupant>();

        if (!grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell? scanCell))
            return Enumerable.Empty<IVoxelOccupant>();

        return scanCell!.IsOccupied
            ? scanCell.GetConditionalOccupantsFor(targetVoxel.WorldIndex, occupantCondition, groupCondition)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    #endregion

    #region Private Methods

    private static IEnumerable<IVoxelOccupant> ScanRadiusIterator(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        Func<IVoxelOccupant, bool>? occupantCondition,
        Func<byte, bool>? groupCondition)
    {
        SwiftList<IVoxelOccupant> results = SwiftListPool<IVoxelOccupant>.Shared.Rent();

        try
        {
            ScanRadiusInto(world, position, radius, results, occupantCondition, groupCondition);

            foreach (IVoxelOccupant result in results)
            {
                yield return result;
            }
        }
        finally
        {
            SwiftListPool<IVoxelOccupant>.Shared.Release(results);
        }
    }

    private static IEnumerable<IVoxelOccupant> ScanRadius2dIterator(
        GridWorld world,
        Vector2d position,
        Fixed64 radius,
        Fixed64 layerY,
        Func<IVoxelOccupant, bool>? occupantCondition,
        Func<byte, bool>? groupCondition)
    {
        SwiftList<IVoxelOccupant> results = SwiftListPool<IVoxelOccupant>.Shared.Rent();

        try
        {
            ScanRadiusInto(world, position, radius, results, layerY, occupantCondition, groupCondition);

            foreach (IVoxelOccupant result in results)
            {
                yield return result;
            }
        }
        finally
        {
            SwiftListPool<IVoxelOccupant>.Shared.Release(results);
        }
    }

    private static void AddRadiusOccupantsTo(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        SwiftList<IVoxelOccupant> results,
        Func<IVoxelOccupant, bool>? occupantCondition,
        Func<byte, bool>? groupCondition)
    {
        Fixed64 squaredRadius = radius * radius;
        Vector3d boundsMin = position - radius;
        Vector3d boundsMax = position + radius;
        SwiftList<ScanCell> scanCells = SwiftListPool<ScanCell>.Shared.Rent();

        try
        {
            GridTracer.AddCoveredScanCellsTo(world, boundsMin, boundsMax, scanCells);

            for (int i = 0; i < scanCells.Count; i++)
            {
                ScanCell scanCell = scanCells[i];
                if (scanCell.IsOccupied)
                    scanCell.AddOccupantsWithinRadiusTo(results, position, squaredRadius, occupantCondition, groupCondition);
            }
        }
        finally
        {
            SwiftListPool<ScanCell>.Shared.Release(scanCells);
        }
    }

    private static void AddRadiusOccupants2dTo(
        GridWorld world,
        Vector2d position,
        Fixed64 radius,
        Fixed64 layerY,
        SwiftList<IVoxelOccupant> results,
        Func<IVoxelOccupant, bool>? occupantCondition,
        Func<byte, bool>? groupCondition)
    {
        Fixed64 squaredRadius = radius * radius;
        Vector3d center = GridPlane2d.ToWorld(position, layerY);
        Vector3d boundsMin = new(center.X - radius, center.Y, center.Z - radius);
        Vector3d boundsMax = new(center.X + radius, center.Y, center.Z + radius);
        SwiftList<ScanCell> scanCells = SwiftListPool<ScanCell>.Shared.Rent();

        try
        {
            GridTracer.AddCoveredScanCellsTo(world, boundsMin, boundsMax, scanCells);

            for (int i = 0; i < scanCells.Count; i++)
            {
                ScanCell scanCell = scanCells[i];
                if (scanCell.IsOccupied)
                    scanCell.AddOccupantsWithinRadius2dTo(
                        results,
                        center,
                        GetScanCellLocalLayerY(world, scanCell, layerY),
                        squaredRadius,
                        occupantCondition,
                        groupCondition);
            }
        }
        finally
        {
            SwiftListPool<ScanCell>.Shared.Release(scanCells);
        }
    }

    private static void AddRadiusOccupantsTo(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        SwiftList<IVoxelOccupant> results,
        GridScanScratch scratch,
        Func<IVoxelOccupant, bool>? occupantCondition,
        Func<byte, bool>? groupCondition)
    {
        Fixed64 squaredRadius = radius * radius;
        Vector3d boundsMin = position - radius;
        Vector3d boundsMax = position + radius;

        GridTracer.AddCoveredScanCellsTo(world, boundsMin, boundsMax, scratch.ScanCells, scratch);

        for (int i = 0; i < scratch.ScanCells.Count; i++)
        {
            ScanCell scanCell = scratch.ScanCells[i];
            if (scanCell.IsOccupied)
                scanCell.AddOccupantsWithinRadiusTo(results, position, squaredRadius, occupantCondition, groupCondition);
        }
    }

    private static void AddRadiusOccupants2dTo(
        GridWorld world,
        Vector2d position,
        Fixed64 radius,
        Fixed64 layerY,
        SwiftList<IVoxelOccupant> results,
        GridScanScratch scratch,
        Func<IVoxelOccupant, bool>? occupantCondition,
        Func<byte, bool>? groupCondition)
    {
        Fixed64 squaredRadius = radius * radius;
        Vector3d center = GridPlane2d.ToWorld(position, layerY);
        Vector3d boundsMin = new(center.X - radius, center.Y, center.Z - radius);
        Vector3d boundsMax = new(center.X + radius, center.Y, center.Z + radius);

        GridTracer.AddCoveredScanCellsTo(world, boundsMin, boundsMax, scratch.ScanCells, scratch);

        for (int i = 0; i < scratch.ScanCells.Count; i++)
        {
            ScanCell scanCell = scratch.ScanCells[i];
            if (scanCell.IsOccupied)
                scanCell.AddOccupantsWithinRadius2dTo(
                    results,
                    center,
                    GetScanCellLocalLayerY(world, scanCell, layerY),
                    squaredRadius,
                    occupantCondition,
                    groupCondition);
        }
    }

    private static void AddRadiusOccupantsTo<T>(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        SwiftList<T> results,
        Func<IVoxelOccupant, bool>? occupantCondition,
        Func<byte, bool>? groupCondition) where T : IVoxelOccupant
    {
        Fixed64 squaredRadius = radius * radius;
        Vector3d boundsMin = position - radius;
        Vector3d boundsMax = position + radius;
        SwiftList<ScanCell> scanCells = SwiftListPool<ScanCell>.Shared.Rent();

        try
        {
            GridTracer.AddCoveredScanCellsTo(world, boundsMin, boundsMax, scanCells);

            for (int i = 0; i < scanCells.Count; i++)
            {
                ScanCell scanCell = scanCells[i];
                if (scanCell.IsOccupied)
                    scanCell.AddOccupantsWithinRadiusTo(results, position, squaredRadius, occupantCondition, groupCondition);
            }
        }
        finally
        {
            SwiftListPool<ScanCell>.Shared.Release(scanCells);
        }
    }

    private static void AddRadiusOccupants2dTo<T>(
        GridWorld world,
        Vector2d position,
        Fixed64 radius,
        Fixed64 layerY,
        SwiftList<T> results,
        Func<IVoxelOccupant, bool>? occupantCondition,
        Func<byte, bool>? groupCondition) where T : IVoxelOccupant
    {
        Fixed64 squaredRadius = radius * radius;
        Vector3d center = GridPlane2d.ToWorld(position, layerY);
        Vector3d boundsMin = new(center.X - radius, center.Y, center.Z - radius);
        Vector3d boundsMax = new(center.X + radius, center.Y, center.Z + radius);
        SwiftList<ScanCell> scanCells = SwiftListPool<ScanCell>.Shared.Rent();

        try
        {
            GridTracer.AddCoveredScanCellsTo(world, boundsMin, boundsMax, scanCells);

            for (int i = 0; i < scanCells.Count; i++)
            {
                ScanCell scanCell = scanCells[i];
                if (scanCell.IsOccupied)
                    scanCell.AddOccupantsWithinRadius2dTo(
                        results,
                        center,
                        GetScanCellLocalLayerY(world, scanCell, layerY),
                        squaredRadius,
                        occupantCondition,
                        groupCondition);
            }
        }
        finally
        {
            SwiftListPool<ScanCell>.Shared.Release(scanCells);
        }
    }

    private static void AddRadiusOccupantsTo<T>(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        SwiftList<T> results,
        GridScanScratch scratch,
        Func<IVoxelOccupant, bool>? occupantCondition,
        Func<byte, bool>? groupCondition) where T : IVoxelOccupant
    {
        Fixed64 squaredRadius = radius * radius;
        Vector3d boundsMin = position - radius;
        Vector3d boundsMax = position + radius;

        GridTracer.AddCoveredScanCellsTo(world, boundsMin, boundsMax, scratch.ScanCells, scratch);

        for (int i = 0; i < scratch.ScanCells.Count; i++)
        {
            ScanCell scanCell = scratch.ScanCells[i];
            if (scanCell.IsOccupied)
                scanCell.AddOccupantsWithinRadiusTo(results, position, squaredRadius, occupantCondition, groupCondition);
        }
    }

    private static void AddRadiusOccupants2dTo<T>(
        GridWorld world,
        Vector2d position,
        Fixed64 radius,
        Fixed64 layerY,
        SwiftList<T> results,
        GridScanScratch scratch,
        Func<IVoxelOccupant, bool>? occupantCondition,
        Func<byte, bool>? groupCondition) where T : IVoxelOccupant
    {
        Fixed64 squaredRadius = radius * radius;
        Vector3d center = GridPlane2d.ToWorld(position, layerY);
        Vector3d boundsMin = new(center.X - radius, center.Y, center.Z - radius);
        Vector3d boundsMax = new(center.X + radius, center.Y, center.Z + radius);

        GridTracer.AddCoveredScanCellsTo(world, boundsMin, boundsMax, scratch.ScanCells, scratch);

        for (int i = 0; i < scratch.ScanCells.Count; i++)
        {
            ScanCell scanCell = scratch.ScanCells[i];
            if (scanCell.IsOccupied)
                scanCell.AddOccupantsWithinRadius2dTo(
                    results,
                    center,
                    GetScanCellLocalLayerY(world, scanCell, layerY),
                    squaredRadius,
                    occupantCondition,
                    groupCondition);
        }
    }

    private static int GetScanCellLocalLayerY(
        GridWorld world,
        ScanCell scanCell,
        Fixed64 layerY)
    {
        world.TryGetGrid(scanCell.GridIndex, out VoxelGrid? grid);
        Vector3d layerProbe = new(grid!.BoundsMin.X, layerY, grid.BoundsMin.Z);
        grid.TryGetVoxelIndex(layerProbe, out VoxelIndex layerIndex);
        return layerIndex.y;
    }

    #endregion
}
