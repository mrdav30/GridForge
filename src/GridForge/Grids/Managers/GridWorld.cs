//=======================================================================
// GridWorld.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using SwiftCollections.Query;

namespace GridForge.Grids;

/// <summary>
/// Owns the mutable runtime state for one GridForge world.
/// </summary>
public sealed partial class GridWorld : IDisposable
{
    #region Constants

    /// <summary>
    /// Maximum number of grids that can be managed within a world.
    /// </summary>
    public const ushort MaxGrids = ushort.MaxValue - 1;

    /// <summary>
    /// The default rectangular cell edge in world units.
    /// </summary>
    public static readonly Fixed64 DefaultRectangularCellSize = Fixed64.One;

    /// <summary>
    /// The default cell size used to tune ordinary-grid lookup.
    /// Oversized grids are indexed automatically outside this tier.
    /// </summary>
    public const int DefaultSpatialGridCellSize = 50;

    private const int BoundaryContactSourceWordCount = (MaxGrids + 63) / 64;
    private const int BoundaryContactSourceSummaryWordCount =
        (BoundaryContactSourceWordCount + 63) / 64;

    #endregion

    #region Properties

    private static readonly Comparison<VoxelIndex> CompareVoxelIndices =
        static (left, right) => left.CompareTo(right);

    /// <summary>
    /// The cell size used to tune ordinary-grid lookup in this world.
    /// Oversized grids are indexed automatically outside this tier.
    /// </summary>
    public int SpatialGridCellSize { get; }

    /// <summary>
    /// Collection of all active grids owned by this world.
    /// </summary>
    public SwiftBucket<VoxelGrid> ActiveGrids { get; }

    /// <summary>
    /// Dictionary mapping exact grid configuration keys to grid indices to prevent duplicate grids.
    /// </summary>
    public SwiftDictionary<GridConfigurationKey, ushort> BoundsTracker { get; }

    /// <summary>
    /// Nonzero process-unique 64-bit runtime allocation token for this active world.
    /// Zero indicates an inactive world.
    /// </summary>
    public long SpawnToken { get; private set; }

    /// <summary>
    /// The current version of the world, incremented on major changes.
    /// </summary>
    public uint Version { get; private set; }

    /// <summary>
    /// The most recent world-local committed change sequence.
    /// </summary>
    public ulong ChangeSequence
    {
        get
        {
            lock (ChangeSyncRoot)
                return _changeSequence;
        }
    }

    /// <summary>
    /// Indicates whether this world is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    internal Fixed64 MaxTopologyCellEdge { get; private set; }

    internal void EnterReadLock() => _gridLock.EnterReadLock();

    internal void ExitReadLock() => _gridLock.ExitReadLock();

    internal bool IsWriteLockHeld => _gridLock.IsWriteLockHeld;

    private static long s_worldAllocationCounter;
    private static long s_obstacleRegistrationCounter;

    private readonly ReaderWriterLockSlim _gridLock = new();
    internal object ChangeSyncRoot { get; } = new object();
    private readonly SwiftQueue<GridCommittedChange> _committedChanges = new SwiftQueue<GridCommittedChange>();
    private readonly SwiftList<ushort> _gridCandidates = new();
    private readonly SwiftDictionary<ushort, SwiftList<ushort>> _boundaryContactTargetsBySource = new();
    private readonly SwiftDictionary<ushort, SwiftList<ushort>> _boundaryContactSourcesByTarget = new();
    private readonly GridSpatialIndex _spatialIndex;
    private ulong[]? _boundaryContactSourceWords;
    private ulong[]? _boundaryContactSourceSummaryWords;
    private int _boundaryContactSourceSummaryLength;
    private long _gridGenerationCounter;
    private ulong _changeSequence;
    private ulong _publishedChangeSequence;
    private bool _isPublishingCommittedChanges;
    private int _committedPublicationOwnerThreadId;
    private int _navigationMaintenanceOwnerThreadId;

    #endregion

    #region Events

    private Action<GridEventInfo>? _onActiveGridAdded;
    private Action<GridEventInfo>? _onActiveGridRemoved;
    private Action<GridEventInfo>? _onActiveGridChange;
    private Action<GridEventInfo>? _onChangeCommitted;
    private Action? _onReset;

    /// <summary>
    /// Event triggered when a new grid is added to this world.
    /// </summary>
    public event Action<GridEventInfo> OnActiveGridAdded
    {
        add
        {
            lock (ChangeSyncRoot)
                _onActiveGridAdded += value;
        }
        remove
        {
            lock (ChangeSyncRoot)
                _onActiveGridAdded -= value;
        }
    }

    /// <summary>
    /// Event triggered when a grid is removed from this world.
    /// </summary>
    public event Action<GridEventInfo> OnActiveGridRemoved
    {
        add
        {
            lock (ChangeSyncRoot)
                _onActiveGridRemoved += value;
        }
        remove
        {
            lock (ChangeSyncRoot)
                _onActiveGridRemoved -= value;
        }
    }

    /// <summary>
    /// Event triggered when a grid in this world undergoes a significant change.
    /// </summary>
    public event Action<GridEventInfo> OnActiveGridChange
    {
        add
        {
            lock (ChangeSyncRoot)
                _onActiveGridChange += value;
        }
        remove
        {
            lock (ChangeSyncRoot)
                _onActiveGridChange -= value;
        }
    }

    /// <summary>
    /// Receives every committed grid lifecycle, sparse-presence, and obstacle mutation in
    /// ascending <see cref="GridEventInfo.ChangeSequence"/> order.
    /// </summary>
    public event Action<GridEventInfo> OnChangeCommitted
    {
        add
        {
            lock (ChangeSyncRoot)
                _onChangeCommitted += value;
        }
        remove
        {
            lock (ChangeSyncRoot)
                _onChangeCommitted -= value;
        }
    }

    /// <summary>
    /// Event triggered when this world is reset.
    /// </summary>
    public event Action OnReset
    {
        add
        {
            lock (ChangeSyncRoot)
                _onReset += value;
        }
        remove
        {
            lock (ChangeSyncRoot)
                _onReset -= value;
        }
    }

    #endregion

    /// <summary>
    /// Initializes a new world with optional ordinary-grid lookup tuning.
    /// </summary>
    /// <param name="spatialGridCellSize">Optional ordinary-grid lookup cell size for this world.</param>
    public GridWorld(int spatialGridCellSize = DefaultSpatialGridCellSize)
    {
        ActiveGrids = new SwiftBucket<VoxelGrid>();
        BoundsTracker = new SwiftDictionary<GridConfigurationKey, ushort>();

        SpatialGridCellSize = ResolveSpatialGridCellSize(spatialGridCellSize);
        _spatialIndex = new GridSpatialIndex(SpatialGridCellSize);
        SpawnToken = RuntimeIdentityAllocator.Allocate(ref s_worldAllocationCounter);
        Version = 1;
        IsActive = true;
    }

    #region Lifecycle

    /// <summary>
    /// Clears all grids and spatial data owned by this world.
    /// </summary>
    /// <param name="deactivate">If true, marks the world inactive and releases its event handlers.</param>
    public void Reset(bool deactivate = false)
    {
        if (!IsActive)
        {
            GridForgeLogger.Channel.Warn($"Grid world not active. Cannot reset an inactive world.");
            return;
        }

        NotifyResetHandlers();
        bool drainCommittedChanges;
        _gridLock.EnterWriteLock();
        try
        {
            lock (ChangeSyncRoot)
            {
                bool wasPublishingCommittedChanges = _isPublishingCommittedChanges;
                ReleaseActiveGrids();
                GridOccupantManager.ClearTrackedOccupancies(this);
                Version++;

                GridEventInfo resetEvent = new GridEventInfo(
                    SpawnToken,
                    ushort.MaxValue,
                    0,
                    default,
                    0,
                    GridEventKind.WorldReset,
                    changeStamp: AllocateChangeStamp());
                EnqueueCommittedChange(new GridCommittedChange(resetEvent));
                drainCommittedChanges = !wasPublishingCommittedChanges;

                if (deactivate)
                {
                    GridOccupantManager.ReleaseTrackedOccupancies(this);
                    IsActive = false;
                }
            }
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }

        if (drainCommittedChanges)
            DrainCommittedChanges();

        if (!deactivate)
            return;

        lock (ChangeSyncRoot)
        {
            SpawnToken = 0;
            _onActiveGridAdded = null;
            _onActiveGridRemoved = null;
            _onActiveGridChange = null;
            _onChangeCommitted = null;
            _onReset = null;
        }
    }

    private void NotifyResetHandlers()
    {
        Action? resetHandlers = _onReset;
        if (resetHandlers == null)
            return;

        var handlerDelegates = resetHandlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action)handlerDelegates[i])();
            }
            catch (Exception ex)
            {
                GridForgeLogger.Channel.Error($"World reset notification error: {ex.Message}");
            }
        }
    }

    private void ReleaseActiveGrids()
    {
        _spatialIndex.Clear();
        ReleaseBoundaryContactPairs();

        foreach (VoxelGrid grid in ActiveGrids)
            Pools.GridPool.Release(grid);

        ActiveGrids.Clear();
        BoundsTracker.Clear();
        MaxTopologyCellEdge = Fixed64.Zero;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Reset(deactivate: true);
        _gridLock.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Grid Management

    /// <summary>
    /// Allocates a nonzero process-unique identity for one obstacle registration lifetime.
    /// </summary>
    /// <returns>A fresh opaque obstacle token.</returns>
    /// <exception cref="InvalidOperationException">The world is inactive or its token space is exhausted.</exception>
    public ObstacleToken AllocateObstacleToken()
    {
        if (!IsActive)
            throw new InvalidOperationException("Cannot allocate an obstacle token from an inactive world.");

        return new ObstacleToken(RuntimeIdentityAllocator.Allocate(ref s_obstacleRegistrationCounter));
    }

    /// <summary>
    /// Captures presence and obstacle state for a sorted requested address span without
    /// enumerating unrelated grids or unrequested physical voxels.
    /// </summary>
    /// <param name="configurationKey">The exact normalized configuration identity to resolve.</param>
    /// <param name="requestedVoxels">Strictly ascending, unique, in-bounds topology-local addresses.</param>
    /// <param name="baseline">The atomic baseline on success.</param>
    /// <returns>True when the requested active grid generation was captured; otherwise false.</returns>
    public bool TryCaptureNavigationBaseline(
        GridConfigurationKey configurationKey,
        ReadOnlySpan<VoxelIndex> requestedVoxels,
        out GridNavigationBaseline? baseline)
    {
        baseline = null;
        if (!IsActive)
            return false;

        if (Volatile.Read(ref _navigationMaintenanceOwnerThreadId)
            == Environment.CurrentManagedThreadId)
        {
            return TryCaptureNavigationBaselineCore(configurationKey, requestedVoxels, out baseline);
        }

        _gridLock.EnterReadLock();
        try
        {
            lock (ChangeSyncRoot)
                return TryCaptureNavigationBaselineCore(configurationKey, requestedVoxels, out baseline);
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Executes one short navigation maintenance snapshot while grid mutations are frozen.
    /// Committed-change prefix detachment and all required navigation baseline captures can
    /// therefore observe one deterministic world state.
    /// </summary>
    /// <param name="maintenance">The non-mutating maintenance callback to execute.</param>
    /// <remarks>
    /// The callback may call <see cref="TryCaptureNavigationBaseline"/> without lock recursion.
    /// It must not mutate this world, wait for code that may mutate this world, or retain live
    /// grid/voxel references beyond the callback. This method must not be called from a committed-
    /// change notification handler. Those handlers remain outside the mutation lock and may enqueue
    /// represented events after this snapshot completes.
    /// </remarks>
    public void ExecuteNavigationMaintenanceSnapshot(Action maintenance)
    {
        SwiftThrowHelper.ThrowIfNull(maintenance, nameof(maintenance));
        ThrowIfNavigationMaintenanceUnavailable();

        while (true)
        {
            if (TryEnterNavigationMaintenanceSnapshot())
            {
                try
                {
                    maintenance();
                    return;
                }
                finally
                {
                    ExitNavigationMaintenanceSnapshot();
                }
            }

            WaitForPublishedChangePrefix();
        }
    }

    /// <summary>
    /// Begins or restarts a bounded exact boundary-contact query against the current committed world state.
    /// </summary>
    /// <param name="cursor">The caller-owned cursor to reset and bind.</param>
    public void BeginBoundaryContacts(GridBoundaryContactCursor cursor)
    {
        SwiftThrowHelper.ThrowIfNull(cursor, nameof(cursor));
        ThrowIfNavigationMaintenanceUnavailable();

        while (true)
        {
            if (TryEnterNavigationMaintenanceSnapshot())
            {
                try
                {
                    cursor.Begin(SpawnToken, Version, _changeSequence);
                    return;
                }
                finally
                {
                    ExitNavigationMaintenanceSnapshot();
                }
            }

            WaitForPublishedChangePrefix();
        }
    }

    /// <summary>
    /// Begins or restarts a bounded exact boundary-contact query restricted to one active grid.
    /// </summary>
    /// <param name="configurationKey">The exact normalized configuration identity to resolve.</param>
    /// <param name="cursor">The caller-owned cursor to reset and bind.</param>
    /// <returns>True when the requested active grid was bound; otherwise false.</returns>
    public bool TryBeginBoundaryContacts(
        GridConfigurationKey configurationKey,
        GridBoundaryContactCursor cursor)
    {
        SwiftThrowHelper.ThrowIfNull(cursor, nameof(cursor));
        ThrowIfNavigationMaintenanceUnavailable();

        while (true)
        {
            if (TryEnterNavigationMaintenanceSnapshot())
            {
                try
                {
                    if (!BoundsTracker.TryGetValue(configurationKey, out ushort gridIndex)
                        || !ActiveGrids.IsAllocated(gridIndex))
                    {
                        cursor.MarkStale();
                        return false;
                    }

                    VoxelGrid grid = ActiveGrids[gridIndex];
                    if (grid.Configuration.ToGridKey() != configurationKey)
                    {
                        cursor.MarkStale();
                        return false;
                    }

                    cursor.BeginFiltered(
                        SpawnToken,
                        Version,
                        _changeSequence,
                        gridIndex,
                        grid.SpawnToken,
                        grid.ChangeHighWaterSequence);
                    return true;
                }
                finally
                {
                    ExitNavigationMaintenanceSnapshot();
                }
            }

            WaitForPublishedChangePrefix();
        }
    }

    /// <summary>
    /// Advances a bounded exact boundary-contact query under one short navigation-maintenance snapshot.
    /// </summary>
    /// <param name="cursor">The caller-owned cursor previously begun through this world.</param>
    /// <param name="results">Caller-owned storage for contacts emitted by this chunk.</param>
    /// <param name="candidateProbeLimit">The maximum pair, source-address, and target probes for this chunk.</param>
    /// <param name="outputLimit">The maximum contacts to write during this chunk.</param>
    /// <param name="candidateProbesConsumed">The exact number of candidate probes consumed by this chunk.</param>
    /// <param name="outputCount">The number of contacts written to <paramref name="results"/>.</param>
    /// <returns>The resulting cursor state.</returns>
    /// <remarks>
    /// A <see cref="GridBoundaryContactCursorStatus.Stale"/> result writes no contacts and resets the
    /// cursor ordinal. The caller must discard every contact returned since the preceding begin.
    /// Completed cursors remain bound and are revalidated on every later call, including zero-budget calls.
    /// </remarks>
    public GridBoundaryContactCursorStatus AdvanceBoundaryContacts(
        GridBoundaryContactCursor cursor,
        Span<VoxelContactManifold> results,
        int candidateProbeLimit,
        int outputLimit,
        out int candidateProbesConsumed,
        out int outputCount)
    {
        SwiftThrowHelper.ThrowIfNull(cursor, nameof(cursor));
        SwiftThrowHelper.ThrowIfNegative(candidateProbeLimit, nameof(candidateProbeLimit));
        SwiftThrowHelper.ThrowIfNegative(outputLimit, nameof(outputLimit));
        if (outputLimit > results.Length)
            throw new ArgumentOutOfRangeException(nameof(outputLimit));

        return AdvanceBoundaryContactsUnderGate(
            cursor,
            results,
            default,
            includeConfigurationKeys: false,
            candidateProbeLimit,
            outputLimit,
            out candidateProbesConsumed,
            out outputCount);
    }

    /// <summary>
    /// Advances a bounded exact boundary-contact query and emits durable grid identities with each contact.
    /// </summary>
    /// <param name="cursor">The caller-owned cursor previously begun through this world.</param>
    /// <param name="results">Caller-owned storage for contacts and their normalized grid identities.</param>
    /// <param name="candidateProbeLimit">The maximum pair, source-address, and target probes for this chunk.</param>
    /// <param name="outputLimit">The maximum contacts to write during this chunk.</param>
    /// <param name="candidateProbesConsumed">The exact number of candidate probes consumed by this chunk.</param>
    /// <param name="outputCount">The number of contacts written to <paramref name="results"/>.</param>
    /// <returns>The resulting cursor state.</returns>
    /// <remarks>
    /// Every emitted identity belongs to <see cref="GridBoundaryContactCursor.RunStamp"/>.
    /// A stale result writes no contacts and resets that stamp to its default value.
    /// </remarks>
    public GridBoundaryContactCursorStatus AdvanceBoundaryContacts(
        GridBoundaryContactCursor cursor,
        Span<GridBoundaryContact> results,
        int candidateProbeLimit,
        int outputLimit,
        out int candidateProbesConsumed,
        out int outputCount)
    {
        SwiftThrowHelper.ThrowIfNull(cursor, nameof(cursor));
        SwiftThrowHelper.ThrowIfNegative(candidateProbeLimit, nameof(candidateProbeLimit));
        SwiftThrowHelper.ThrowIfNegative(outputLimit, nameof(outputLimit));
        if (outputLimit > results.Length)
            throw new ArgumentOutOfRangeException(nameof(outputLimit));

        return AdvanceBoundaryContactsUnderGate(
            cursor,
            default,
            results,
            includeConfigurationKeys: true,
            candidateProbeLimit,
            outputLimit,
            out candidateProbesConsumed,
            out outputCount);
    }

    private GridBoundaryContactCursorStatus AdvanceBoundaryContactsUnderGate(
        GridBoundaryContactCursor cursor,
        Span<VoxelContactManifold> manifoldResults,
        Span<GridBoundaryContact> boundResults,
        bool includeConfigurationKeys,
        int candidateProbeLimit,
        int outputLimit,
        out int candidateProbesConsumed,
        out int outputCount)
    {
        ThrowIfNavigationMaintenanceUnavailable();
        candidateProbesConsumed = 0;
        outputCount = 0;
        while (true)
        {
            if (TryEnterNavigationMaintenanceSnapshot())
            {
                try
                {
                    return AdvanceBoundaryContactsCore(
                        cursor,
                        manifoldResults,
                        boundResults,
                        includeConfigurationKeys,
                        candidateProbeLimit,
                        outputLimit,
                        out candidateProbesConsumed,
                        out outputCount);
                }
                finally
                {
                    ExitNavigationMaintenanceSnapshot();
                }
            }

            WaitForPublishedChangePrefix();
        }
    }

    private GridBoundaryContactCursorStatus AdvanceBoundaryContactsCore(
        GridBoundaryContactCursor cursor,
        Span<VoxelContactManifold> manifoldResults,
        Span<GridBoundaryContact> boundResults,
        bool includeConfigurationKeys,
        int candidateProbeLimit,
        int outputLimit,
        out int candidateProbesConsumed,
        out int outputCount)
    {
        candidateProbesConsumed = 0;
        outputCount = 0;
        if (!IsBoundaryContactCursorCurrent(cursor))
            return cursor.MarkStale();

        if (cursor.CurrentStatus == GridBoundaryContactCursorStatus.Complete)
            return cursor.CurrentStatus;

        while (true)
        {
            if (cursor.HasPendingContact)
            {
                if (outputCount == outputLimit)
                    return GridBoundaryContactCursorStatus.More;

                if (includeConfigurationKeys)
                {
                    boundResults[outputCount++] = new GridBoundaryContact(
                        cursor.SourceConfigurationKey,
                        cursor.TargetConfigurationKey,
                        cursor.PendingContact);
                }
                else
                {
                    manifoldResults[outputCount++] = cursor.PendingContact;
                }
                cursor.PendingContact = default;
                cursor.HasPendingContact = false;
                if (outputCount == outputLimit)
                    return GridBoundaryContactCursorStatus.More;
            }

            switch (cursor.Stage)
            {
                case GridBoundaryContactCursor.TraversalStage.Pair:
                    if (cursor.IsFiltered)
                    {
                        if (!TryAdvanceFilteredBoundaryContactPair(
                                cursor,
                                candidateProbeLimit,
                                ref candidateProbesConsumed,
                                out ushort filteredSource,
                                out ushort filteredTarget))
                        {
                            return cursor.CurrentStatus;
                        }

                        if (!TryBindBoundaryContactPair(cursor, filteredSource, filteredTarget))
                            return cursor.MarkStale();
                        cursor.Stage = GridBoundaryContactCursor.TraversalStage.Source;
                        continue;
                    }

                    if (!cursor.HasPairSource)
                    {
                        if (cursor.PairSourceWord != 0)
                        {
                            int sourceBit = GetTrailingZeroCount(cursor.PairSourceWord);
                            cursor.PairSourceWord &= cursor.PairSourceWord - 1UL;
                            cursor.PairSourceGridIndex = (ushort)(
                                (cursor.PairSourceWordIndex << 6) + sourceBit);
                            cursor.PairTargetOrdinal = 0;
                            cursor.HasPairSource = true;
                            continue;
                        }

                        if (cursor.PairSourceSummaryWord != 0)
                        {
                            if (candidateProbesConsumed == candidateProbeLimit)
                                return GridBoundaryContactCursorStatus.More;

                            int wordBit = GetTrailingZeroCount(cursor.PairSourceSummaryWord);
                            cursor.PairSourceSummaryWord &= cursor.PairSourceSummaryWord - 1UL;
                            cursor.PairSourceWordIndex =
                                ((cursor.PairSourceSummaryWordIndex - 1) << 6) + wordBit;
                            cursor.PairSourceWord = _boundaryContactSourceWords![cursor.PairSourceWordIndex];
                            ConsumeBoundaryContactProbe(cursor, ref candidateProbesConsumed);
                            continue;
                        }

                        if (_boundaryContactSourceSummaryWords == null
                            || cursor.PairSourceSummaryWordIndex
                                >= _boundaryContactSourceSummaryLength)
                        {
                            cursor.CurrentStatus = GridBoundaryContactCursorStatus.Complete;
                            return cursor.CurrentStatus;
                        }

                        if (candidateProbesConsumed == candidateProbeLimit)
                            return GridBoundaryContactCursorStatus.More;

                        cursor.PairSourceSummaryWord = _boundaryContactSourceSummaryWords[
                            cursor.PairSourceSummaryWordIndex++];
                        ConsumeBoundaryContactProbe(cursor, ref candidateProbesConsumed);
                        continue;
                    }

                    if (!_boundaryContactTargetsBySource.TryGetValue(
                            cursor.PairSourceGridIndex,
                            out SwiftList<ushort>? pairTargets)
                        || cursor.PairTargetOrdinal >= pairTargets.Count)
                    {
                        cursor.HasPairSource = false;
                        continue;
                    }

                    if (candidateProbesConsumed == candidateProbeLimit)
                        return GridBoundaryContactCursorStatus.More;

                    ushort pairTarget = pairTargets[cursor.PairTargetOrdinal++];
                    ConsumeBoundaryContactProbe(cursor, ref candidateProbesConsumed);
                    if (!TryBindBoundaryContactPair(
                            cursor,
                            cursor.PairSourceGridIndex,
                            pairTarget))
                    {
                        return cursor.MarkStale();
                    }
                    cursor.Stage = GridBoundaryContactCursor.TraversalStage.Source;
                    continue;

                case GridBoundaryContactCursor.TraversalStage.Source:
                    VoxelGrid sourceGrid = ActiveGrids[cursor.SourceGridIndex];
                    VoxelGrid targetGrid = ActiveGrids[cursor.TargetGridIndex];
                    if (!cursor.HasSourceRange)
                    {
                        cursor.ClearPairProgress();
                        continue;
                    }

                    if (candidateProbesConsumed == candidateProbeLimit)
                        return GridBoundaryContactCursorStatus.More;

                    VoxelIndex sourceIndex = cursor.SourceAddress;
                    cursor.HasSourceRange = AdvanceBoundaryContactAddress(
                        ref cursor.SourceAddress,
                        cursor.SourceMinimum,
                        cursor.SourceMaximum);
                    ConsumeBoundaryContactProbe(cursor, ref candidateProbesConsumed);
                    if (!TryCreateTopologyPrism(sourceGrid, sourceIndex, out cursor.SourcePrism)
                        || !TopologyVoxelRangeUtility.TryGetCandidateRange(
                            targetGrid,
                            cursor.SourcePrism.GetAabb().Expand(targetGrid.Topology.MaxCellEdge),
                            out cursor.TargetMinimum,
                            out cursor.TargetMaximum))
                    {
                        continue;
                    }

                    cursor.TargetAddress = cursor.TargetMinimum;
                    cursor.Stage = GridBoundaryContactCursor.TraversalStage.Target;
                    continue;

                default:
                    if (candidateProbesConsumed == candidateProbeLimit)
                        return GridBoundaryContactCursorStatus.More;

                    ProbeBoundaryContactTarget(cursor, ref candidateProbesConsumed);
                    continue;
            }
        }
    }

    private bool IsBoundaryContactCursorCurrent(GridBoundaryContactCursor cursor)
    {
        if (cursor.CurrentStatus == GridBoundaryContactCursorStatus.Stale
            || cursor.WorldSpawnToken != SpawnToken
            || cursor.WorldVersion != Version
            || cursor.WorldChangeSequence != _changeSequence)
        {
            return false;
        }

        if (cursor.IsFiltered
            && (!ActiveGrids.IsAllocated(cursor.FilterGridIndex)
                || ActiveGrids[cursor.FilterGridIndex].SpawnToken != cursor.FilterGridSpawnToken
                || ActiveGrids[cursor.FilterGridIndex].ChangeHighWaterSequence
                    != cursor.FilterGridHighWaterSequence))
        {
            return false;
        }

        if (cursor.SourceGridSpawnToken == 0)
            return true;

        return ActiveGrids.IsAllocated(cursor.SourceGridIndex)
            && ActiveGrids.IsAllocated(cursor.TargetGridIndex)
            && ActiveGrids[cursor.SourceGridIndex].SpawnToken == cursor.SourceGridSpawnToken
            && ActiveGrids[cursor.TargetGridIndex].SpawnToken == cursor.TargetGridSpawnToken
            && ActiveGrids[cursor.SourceGridIndex].ChangeHighWaterSequence
                == cursor.SourceGridHighWaterSequence
            && ActiveGrids[cursor.TargetGridIndex].ChangeHighWaterSequence
                == cursor.TargetGridHighWaterSequence;
    }

    private bool TryAdvanceFilteredBoundaryContactPair(
        GridBoundaryContactCursor cursor,
        int candidateProbeLimit,
        ref int candidateProbesConsumed,
        out ushort sourceGridIndex,
        out ushort targetGridIndex)
    {
        sourceGridIndex = 0;
        targetGridIndex = 0;
        while (cursor.FilteredPairPhase < 2)
        {
            SwiftDictionary<ushort, SwiftList<ushort>> rows = cursor.FilteredPairPhase == 0
                ? _boundaryContactSourcesByTarget
                : _boundaryContactTargetsBySource;
            if (!cursor.HasFilteredPairRow)
            {
                if (candidateProbesConsumed == candidateProbeLimit)
                    return false;

                cursor.FilteredPairRowCount = rows.TryGetValue(
                    cursor.FilterGridIndex,
                    out SwiftList<ushort>? row)
                    ? row.Count
                    : 0;
                cursor.FilteredPairRowOrdinal = 0;
                if (cursor.FilteredPairRowCount != 0)
                {
                    cursor.PendingFilteredGridIndex = row![0];
                    cursor.FilteredPairRowOrdinal = 1;
                    cursor.HasPendingFilteredPair = true;
                }
                cursor.HasFilteredPairRow = true;
                ConsumeBoundaryContactProbe(cursor, ref candidateProbesConsumed);
            }

            if (cursor.HasPendingFilteredPair)
            {
                if (candidateProbesConsumed == candidateProbeLimit)
                    return false;

                ushort incidentGridIndex = cursor.PendingFilteredGridIndex;
                cursor.PendingFilteredGridIndex = 0;
                cursor.HasPendingFilteredPair = false;
                ConsumeBoundaryContactProbe(cursor, ref candidateProbesConsumed);
                if (cursor.FilteredPairPhase == 0)
                {
                    sourceGridIndex = incidentGridIndex;
                    targetGridIndex = cursor.FilterGridIndex;
                }
                else
                {
                    sourceGridIndex = cursor.FilterGridIndex;
                    targetGridIndex = incidentGridIndex;
                }

                return true;
            }

            if (cursor.FilteredPairRowOrdinal < cursor.FilteredPairRowCount)
            {
                if (candidateProbesConsumed == candidateProbeLimit)
                    return false;

                if (!rows.TryGetValue(cursor.FilterGridIndex, out SwiftList<ushort>? row)
                    || row.Count != cursor.FilteredPairRowCount)
                {
                    cursor.MarkStale();
                    return false;
                }

                cursor.PendingFilteredGridIndex = row[cursor.FilteredPairRowOrdinal++];
                cursor.HasPendingFilteredPair = true;
                ConsumeBoundaryContactProbe(cursor, ref candidateProbesConsumed);
                continue;
            }

            cursor.FilteredPairPhase++;
            cursor.FilteredPairRowCount = 0;
            cursor.FilteredPairRowOrdinal = 0;
            cursor.HasFilteredPairRow = false;
        }

        cursor.CurrentStatus = GridBoundaryContactCursorStatus.Complete;
        return false;
    }

    private bool TryBindBoundaryContactPair(
        GridBoundaryContactCursor cursor,
        ushort sourceGridIndex,
        ushort targetGridIndex)
    {
        if (!ActiveGrids.IsAllocated(sourceGridIndex)
            || !ActiveGrids.IsAllocated(targetGridIndex))
        {
            return false;
        }

        VoxelGrid sourceGrid = ActiveGrids[sourceGridIndex];
        VoxelGrid targetGrid = ActiveGrids[targetGridIndex];
        cursor.SourceGridIndex = sourceGridIndex;
        cursor.TargetGridIndex = targetGridIndex;
        cursor.SourceGridSpawnToken = sourceGrid.SpawnToken;
        cursor.TargetGridSpawnToken = targetGrid.SpawnToken;
        cursor.SourceGridHighWaterSequence = sourceGrid.ChangeHighWaterSequence;
        cursor.TargetGridHighWaterSequence = targetGrid.ChangeHighWaterSequence;
        cursor.SourceConfigurationKey = sourceGrid.Configuration.ToGridKey();
        cursor.TargetConfigurationKey = targetGrid.Configuration.ToGridKey();

        if (!TryCreateBoundaryContactEnvelope(targetGrid, out FixedBoundVolume targetEnvelope)
            || !TryCreateTopologyPrism(sourceGrid, default, out GridCellPrism firstSourcePrism))
        {
            return false;
        }

        TopologyVoxelAabb firstSourceBounds = firstSourcePrism.GetAabb();
        Vector3d lowerExtent = sourceGrid.BoundsMin - firstSourceBounds.Min;
        Vector3d upperExtent = firstSourceBounds.Max - sourceGrid.BoundsMin;
        var sourceCandidateBounds = new TopologyVoxelAabb(
            targetEnvelope.Min - upperExtent,
            targetEnvelope.Max + lowerExtent);
        cursor.HasSourceRange = TopologyVoxelRangeUtility.TryGetCandidateRange(
            sourceGrid,
            sourceCandidateBounds,
            out cursor.SourceMinimum,
            out cursor.SourceMaximum);
        cursor.SourceAddress = cursor.SourceMinimum;
        return true;
    }

    private void ProbeBoundaryContactTarget(
        GridBoundaryContactCursor cursor,
        ref int candidateProbesConsumed)
    {
        VoxelGrid targetGrid = ActiveGrids[cursor.TargetGridIndex];
        VoxelIndex targetIndex = cursor.TargetAddress;
        if (!AdvanceBoundaryContactAddress(
                ref cursor.TargetAddress,
                cursor.TargetMinimum,
                cursor.TargetMaximum))
        {
            cursor.Stage = GridBoundaryContactCursor.TraversalStage.Source;
        }
        ConsumeBoundaryContactProbe(cursor, ref candidateProbesConsumed);

        if (!TryCreateTopologyPrism(targetGrid, targetIndex, out GridCellPrism targetPrism))
            return;

        VoxelContactManifold contact = GridCellGeometry.GetContact(cursor.SourcePrism, targetPrism);
        if (contact.Kind != VoxelContactKind.Separated)
        {
            cursor.PendingContact = contact;
            cursor.HasPendingContact = true;
        }
    }

    private static bool AdvanceBoundaryContactAddress(
        ref VoxelIndex address,
        VoxelIndex minimum,
        VoxelIndex maximum)
    {
        if (address.z < maximum.z)
        {
            address.z++;
            return true;
        }

        address.z = minimum.z;
        if (address.y < maximum.y)
        {
            address.y++;
            return true;
        }

        address.y = minimum.y;
        if (address.x < maximum.x)
        {
            address.x++;
            return true;
        }

        return false;
    }

    private bool TryCreateTopologyPrism(
        VoxelGrid grid,
        VoxelIndex index,
        out GridCellPrism prism)
    {
        return GridCellGeometry.TryCreatePrism(
            grid.Configuration.TopologyKind,
            grid.Configuration.TopologyMetrics,
            grid.GetWorldPosition(index),
            new WorldVoxelIndex(SpawnToken, grid.GridIndex, grid.SpawnToken, index),
            out prism);
    }

    private static int GetTrailingZeroCount(ulong value)
    {
        int count = 0;
        while ((value & 1UL) == 0)
        {
            value >>= 1;
            count++;
        }

        return count;
    }

    private static void ConsumeBoundaryContactProbe(
        GridBoundaryContactCursor cursor,
        ref int candidateProbesConsumed)
    {
        candidateProbesConsumed++;
        if (cursor.CandidateOrdinal != ulong.MaxValue)
            cursor.CandidateOrdinal++;
    }

    private void ThrowIfNavigationMaintenanceUnavailable()
    {
        SwiftThrowHelper.ThrowIfTrue(
            !IsActive,
            message: "Cannot capture navigation maintenance state from an inactive world.");
        SwiftThrowHelper.ThrowIfTrue(
            Volatile.Read(ref _committedPublicationOwnerThreadId)
                == Environment.CurrentManagedThreadId,
            message: "Cannot enter navigation maintenance from a committed-change notification handler.");
    }

    private bool TryEnterNavigationMaintenanceSnapshot()
    {
        _gridLock.EnterReadLock();
        Monitor.Enter(ChangeSyncRoot);
        if (_publishedChangeSequence != _changeSequence)
        {
            Monitor.Exit(ChangeSyncRoot);
            _gridLock.ExitReadLock();
            return false;
        }

        _navigationMaintenanceOwnerThreadId = Environment.CurrentManagedThreadId;
        return true;
    }

    private void ExitNavigationMaintenanceSnapshot()
    {
        _navigationMaintenanceOwnerThreadId = 0;
        Monitor.Exit(ChangeSyncRoot);
        _gridLock.ExitReadLock();
    }

    private void WaitForPublishedChangePrefix()
    {
        // A committed handler may legally perform a reentrant structural mutation. Never
        // wait for that handler while holding the read lock it needs to promote past.
        lock (ChangeSyncRoot)
        {
            while (_publishedChangeSequence != _changeSequence)
                Monitor.Wait(ChangeSyncRoot);
        }
    }

    /// <summary>
    /// Atomically attaches a committed-change listener and captures a requested-address baseline.
    /// Events with a sequence greater than the baseline high-water mark are the only events the
    /// caller applies after initialization.
    /// </summary>
    /// <param name="configurationKey">The exact normalized configuration identity to resolve.</param>
    /// <param name="requestedVoxels">Strictly ascending, unique, in-bounds topology-local addresses.</param>
    /// <param name="onChangeCommitted">The committed-change listener to attach.</param>
    /// <param name="subscription">The owned subscription and atomic baseline on success.</param>
    /// <returns>True when attachment and capture both succeeded; otherwise false.</returns>
    public bool TrySubscribeNavigationChanges(
        GridConfigurationKey configurationKey,
        ReadOnlySpan<VoxelIndex> requestedVoxels,
        Action<GridEventInfo> onChangeCommitted,
        out GridNavigationChangeSubscription? subscription)
    {
        SwiftThrowHelper.ThrowIfNull(onChangeCommitted, nameof(onChangeCommitted));

        subscription = null;
        if (!IsActive)
            return false;

        _gridLock.EnterReadLock();
        try
        {
            lock (ChangeSyncRoot)
            {
                _onChangeCommitted += onChangeCommitted;
                if (TryCaptureNavigationBaselineCore(configurationKey, requestedVoxels, out GridNavigationBaseline? baseline))
                {
                    subscription = new GridNavigationChangeSubscription(this, onChangeCommitted, baseline!);
                    return true;
                }

                _onChangeCommitted -= onChangeCommitted;
                return false;
            }
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }

    private bool TryCaptureNavigationBaselineCore(
        GridConfigurationKey configurationKey,
        ReadOnlySpan<VoxelIndex> requestedVoxels,
        out GridNavigationBaseline? baseline)
    {
        Debug.Assert(Monitor.IsEntered(ChangeSyncRoot));
        baseline = null;
        if (!IsActive
            || !BoundsTracker.TryGetValue(configurationKey, out ushort gridIndex)
            || !ActiveGrids.IsAllocated(gridIndex))
        {
            return false;
        }

        if (!ActiveGrids.IsAllocated(gridIndex))
            return false;

        VoxelGrid grid = ActiveGrids[gridIndex];
        if (grid.Configuration.ToGridKey() != configurationKey
            || !AreNavigationBaselineAddressesValid(grid, requestedVoxels))
        {
            return false;
        }

        NavigationBaselineVoxelState[] states = new NavigationBaselineVoxelState[requestedVoxels.Length];
        for (int i = 0; i < requestedVoxels.Length; i++)
        {
            VoxelIndex requestedVoxel = requestedVoxels[i];
            bool isPresent = grid.TryGetVoxel(requestedVoxel, out Voxel? voxel);
            states[i] = new NavigationBaselineVoxelState(
                requestedVoxel,
                isPresent,
                isPresent ? voxel!.ObstacleCount : (byte)0);
        }

        baseline = new GridNavigationBaseline(
            _changeSequence,
            SpawnToken,
            grid.SpawnToken,
            grid.ChangeHighWaterSequence,
            grid.GridIndex,
            configurationKey,
            states);
        return true;
    }

    private static bool AreNavigationBaselineAddressesValid(
        VoxelGrid grid,
        ReadOnlySpan<VoxelIndex> requestedVoxels)
    {
        for (int i = 0; i < requestedVoxels.Length; i++)
        {
            VoxelIndex requestedVoxel = requestedVoxels[i];
            if (!grid.IsValidVoxelIndex(requestedVoxel.x, requestedVoxel.y, requestedVoxel.z)
                || (i > 0 && requestedVoxels[i - 1].CompareTo(requestedVoxel) >= 0))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Adds a new grid to this world and registers it in the spatial index.
    /// </summary>
    /// <param name="configuration">The grid configuration to normalize and register.</param>
    /// <param name="allocatedIndex">The allocated world-local grid slot on success.</param>
    /// <returns>True if the grid was added; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAddGrid(GridConfiguration configuration, out ushort allocatedIndex) =>
        TryAddGridCore(configuration, null, null, out allocatedIndex);

    /// <summary>
    /// Adds a new grid to this world and materializes the supplied sparse voxel indices when sparse storage is configured.
    /// Dense grids ignore the configured voxel input and materialize every in-bounds voxel.
    /// </summary>
    /// <param name="configuration">The grid configuration to normalize and register.</param>
    /// <param name="configuredVoxels">Grid-local voxel indices to materialize for sparse storage.</param>
    /// <param name="allocatedIndex">The allocated world-local grid slot on success.</param>
    /// <returns>True if the grid was added; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAddGrid(
        GridConfiguration configuration,
        IEnumerable<VoxelIndex>? configuredVoxels,
        out ushort allocatedIndex) =>
        TryAddGridCore(configuration, configuredVoxels, null, out allocatedIndex);

    /// <summary>
    /// Adds a new grid to this world and materializes true cells from the supplied sparse voxel mask when sparse storage is configured.
    /// Dense grids ignore the configured voxel input and materialize every in-bounds voxel.
    /// </summary>
    /// <param name="configuration">The grid configuration to normalize and register.</param>
    /// <param name="configuredVoxels">A [x, y, z] mask whose true values identify sparse voxels to materialize. Sparse masks must match the normalized grid dimensions.</param>
    /// <param name="allocatedIndex">The allocated world-local grid slot on success.</param>
    /// <returns>True if the grid was added; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAddGrid(
        GridConfiguration configuration,
        bool[,,]? configuredVoxels,
        out ushort allocatedIndex) =>
        TryAddGridCore(configuration, null, configuredVoxels, out allocatedIndex);

    private bool TryAddGridCore(
        GridConfiguration configuration,
        IEnumerable<VoxelIndex>? configuredVoxels,
        bool[,,]? configuredVoxelMask,
        out ushort allocatedIndex)
    {
        allocatedIndex = ushort.MaxValue;

        if (!configuration.TryNormalize(out NormalizedGridConfiguration descriptor))
            return false;

        GridConfiguration normalizedConfiguration = descriptor.Configuration;
        IGridTopology topology = descriptor.Topology!;
        GridDimensions dimensions = descriptor.Dimensions;

        if (!TryPrepareConfiguredVoxels(
            normalizedConfiguration,
            dimensions,
            configuredVoxels,
            configuredVoxelMask,
            out VoxelIndex[] preparedVoxels))
        {
            return false;
        }

        if (!IsActive)
        {
            GridForgeLogger.Channel.Error($"Grid world not active. Cannot add grids to an inactive world.");
            return false;
        }

        GridConfigurationKey boundsKey = descriptor.Key;
        VoxelGrid? newGrid = null;
        GridEventInfo addedGridInfo = default;
        bool drainCommittedChanges;

        _gridLock.EnterWriteLock();
        try
        {
            lock (ChangeSyncRoot)
            {
                if (!CanAddGrid() || TryFindExistingGridUnsafe(boundsKey, out allocatedIndex))
                    return false;

                long gridGeneration = RuntimeIdentityAllocator.Allocate(ref _gridGenerationCounter);
                newGrid = Pools.GridPool.Rent();

                allocatedIndex = (ushort)ActiveGrids.Add(newGrid);
                BoundsTracker.Add(boundsKey, allocatedIndex);

                newGrid.Initialize(this, allocatedIndex, gridGeneration, normalizedConfiguration, topology, preparedVoxels);
                UpdateMaxTopologyCellEdge(newGrid.Topology.MaxCellEdge);
                RegisterGrid(newGrid, allocatedIndex);

                Version++;
                addedGridInfo = CreateGridEventInfo(
                    newGrid,
                    GridEventKind.GridAdded,
                    AllocateChangeStamp());
                drainCommittedChanges = EnqueueCommittedChange(new GridCommittedChange(addedGridInfo));
            }
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }

        if (drainCommittedChanges)
            DrainCommittedChanges();
        return true;
    }

    /// <summary>
    /// Removes a grid from this world and updates all references to ensure integrity.
    /// </summary>
    /// <param name="removeIndex">The world-local grid slot to remove.</param>
    /// <returns>True if the grid was removed; otherwise false.</returns>
    public bool TryRemoveGrid(ushort removeIndex)
    {
        if (!IsActive)
            return false;

        VoxelGrid? gridToRemove = null;
        GridEventInfo removedGridInfo = default;
        bool drainCommittedChanges;

        _gridLock.EnterWriteLock();
        try
        {
            lock (ChangeSyncRoot)
            {
                if (!IsActive || !ActiveGrids.IsAllocated(removeIndex))
                    return false;

                gridToRemove = ActiveGrids[removeIndex];
                Fixed64 removedMaxCellEdge = gridToRemove.Topology.MaxCellEdge;
                UnregisterGrid(gridToRemove, removeIndex);
                BoundsTracker.Remove(gridToRemove.Configuration.ToGridKey());
                ActiveGrids.RemoveAt(removeIndex);
                RecalculateMaxTopologyCellEdgeIfNeeded(removedMaxCellEdge);

                Version++;
                removedGridInfo = CreateGridEventInfo(
                    gridToRemove,
                    GridEventKind.GridRemoved,
                    AllocateChangeStamp());
                drainCommittedChanges = EnqueueCommittedChange(new GridCommittedChange(removedGridInfo));
            }
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }

        Pools.GridPool.Release(gridToRemove!);
        if (drainCommittedChanges)
            DrainCommittedChanges();

        if (ActiveGrids.Count == 0)
            ActiveGrids.TrimExcessCapacity();

        return true;
    }

    #endregion

    private bool CanAddGrid()
    {
        if (!IsActive)
        {
            GridForgeLogger.Channel.Error($"Grid world not active. Cannot add grids to an inactive world.");
            return false;
        }

        if ((uint)ActiveGrids.Count >= MaxGrids)
        {
            GridForgeLogger.Channel.Warn($"No more grids can be added at this time.");
            return false;
        }

        return true;
    }

    private static bool TryPrepareConfiguredVoxels(
        GridConfiguration configuration,
        GridDimensions dimensions,
        IEnumerable<VoxelIndex>? configuredVoxels,
        bool[,,]? configuredVoxelMask,
        out VoxelIndex[] preparedVoxels)
    {
        preparedVoxels = Array.Empty<VoxelIndex>();
        if (configuration.StorageKind != GridStorageKind.Sparse)
            return true;

        if (configuredVoxelMask != null)
            return TryPrepareConfiguredVoxelMask(configuredVoxelMask, dimensions, out preparedVoxels);

        return TryPrepareConfiguredVoxelIndices(configuredVoxels, dimensions, out preparedVoxels);
    }

    private static bool TryPrepareConfiguredVoxelMask(
        bool[,,] configuredVoxelMask,
        GridDimensions dimensions,
        out VoxelIndex[] preparedVoxels)
    {
        preparedVoxels = Array.Empty<VoxelIndex>();

        if (configuredVoxelMask.GetLength(0) != dimensions.Width
            || configuredVoxelMask.GetLength(1) != dimensions.Height
            || configuredVoxelMask.GetLength(2) != dimensions.Length)
        {
            GridForgeLogger.Channel.Warn($"Sparse voxel mask dimensions must match normalized grid dimensions.");
            return false;
        }

        int configuredCount = 0;
        for (int x = 0; x < dimensions.Width; x++)
        {
            for (int y = 0; y < dimensions.Height; y++)
            {
                for (int z = 0; z < dimensions.Length; z++)
                {
                    if (configuredVoxelMask[x, y, z])
                        configuredCount++;
                }
            }
        }

        if (configuredCount == 0)
            return true;

        preparedVoxels = new VoxelIndex[configuredCount];
        int index = 0;
        for (int x = 0; x < dimensions.Width; x++)
        {
            for (int y = 0; y < dimensions.Height; y++)
            {
                for (int z = 0; z < dimensions.Length; z++)
                {
                    if (configuredVoxelMask[x, y, z])
                        preparedVoxels[index++] = new VoxelIndex(x, y, z);
                }
            }
        }

        return true;
    }

    private static bool TryPrepareConfiguredVoxelIndices(
        IEnumerable<VoxelIndex>? configuredVoxels,
        GridDimensions dimensions,
        out VoxelIndex[] preparedVoxels)
    {
        preparedVoxels = Array.Empty<VoxelIndex>();
        if (configuredVoxels == null)
            return true;

        SwiftList<VoxelIndex> indices = configuredVoxels is ICollection<VoxelIndex> collection
            ? new SwiftList<VoxelIndex>(collection.Count)
            : new SwiftList<VoxelIndex>();

        foreach (VoxelIndex configuredVoxel in configuredVoxels)
        {
            if (!IsConfiguredVoxelInBounds(configuredVoxel, dimensions))
            {
                GridForgeLogger.Channel.Warn($"Sparse voxel index {configuredVoxel} is outside normalized grid dimensions.");
                return false;
            }

            indices.Add(configuredVoxel);
        }

        if (indices.Count == 0)
            return true;

        preparedVoxels = indices.ToArray();
        Array.Sort(preparedVoxels, CompareVoxelIndices);
        CompactPreparedVoxels(ref preparedVoxels);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsConfiguredVoxelInBounds(VoxelIndex voxelIndex, GridDimensions dimensions) =>
        (uint)voxelIndex.x < (uint)dimensions.Width
        && (uint)voxelIndex.y < (uint)dimensions.Height
        && (uint)voxelIndex.z < (uint)dimensions.Length;

    private static void CompactPreparedVoxels(ref VoxelIndex[] preparedVoxels)
    {
        if (preparedVoxels.Length < 2)
            return;

        int writeIndex = 1;
        VoxelIndex previous = preparedVoxels[0];
        for (int readIndex = 1; readIndex < preparedVoxels.Length; readIndex++)
        {
            VoxelIndex current = preparedVoxels[readIndex];
            if (current == previous)
                continue;

            preparedVoxels[writeIndex++] = current;
            previous = current;
        }

        if (writeIndex != preparedVoxels.Length)
            Array.Resize(ref preparedVoxels, writeIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateMaxTopologyCellEdge(Fixed64 candidate)
    {
        if (candidate > MaxTopologyCellEdge)
            MaxTopologyCellEdge = candidate;
    }

    private void RecalculateMaxTopologyCellEdgeIfNeeded(Fixed64 removedMaxCellEdge)
    {
        if (removedMaxCellEdge < MaxTopologyCellEdge)
            return;

        Fixed64 maxCellEdge = Fixed64.Zero;
        foreach (VoxelGrid grid in ActiveGrids)
        {
            if (grid.Topology.MaxCellEdge > maxCellEdge)
                maxCellEdge = grid.Topology.MaxCellEdge;
        }

        MaxTopologyCellEdge = maxCellEdge;
    }

    private bool TryFindExistingGridUnsafe(GridConfigurationKey boundsKey, out ushort allocatedIndex)
    {
        if (BoundsTracker.TryGetValue(boundsKey, out allocatedIndex))
        {
            GridForgeLogger.Channel.Warn($"A grid with these bounds has already been allocated.");
            return true;
        }

        allocatedIndex = ushort.MaxValue;
        return false;
    }

    private void RegisterGrid(VoxelGrid newGrid, ushort allocatedIndex)
    {
        bool hasContactEnvelope = TryCreateBoundaryContactEnvelope(
            newGrid,
            out FixedBoundVolume contactEnvelope);
        _spatialIndex.Insert(
            allocatedIndex,
            new FixedBoundVolume(newGrid.BoundsMin, newGrid.BoundsMax),
            hasContactEnvelope ? contactEnvelope : null);

        if (hasContactEnvelope)
        {
            _spatialIndex.CollectContactCandidates(contactEnvelope, _gridCandidates);
            for (int i = 0; i < _gridCandidates.Count; i++)
            {
                ushort candidateIndex = _gridCandidates[i];
                if (candidateIndex != allocatedIndex)
                    InsertBoundaryContactPair(allocatedIndex, candidateIndex);
            }
        }

        _spatialIndex.CollectCandidates(
            CreateExpandedBounds(
                newGrid.BoundsMin,
                newGrid.BoundsMax,
                newGrid.Topology.OverlapTolerance),
            ActiveGrids,
            _gridCandidates);

        for (int candidateIndex = 0; candidateIndex < _gridCandidates.Count; candidateIndex++)
        {
            ushort neighborIndex = _gridCandidates[candidateIndex];
            if (neighborIndex == allocatedIndex)
                continue;

            VoxelGrid neighborGrid = ActiveGrids[neighborIndex];
            newGrid.TryAddGridNeighbor(neighborGrid);
            neighborGrid.TryAddGridNeighbor(newGrid);
        }
    }

    private void UnregisterGrid(VoxelGrid gridToRemove, ushort removeIndex)
    {
        _spatialIndex.Remove(removeIndex);
        RemoveBoundaryContactPairs(removeIndex);
        UnlinkGridNeighbors(gridToRemove);
    }

    private static bool TryCreateBoundaryContactEnvelope(
        VoxelGrid grid,
        out FixedBoundVolume envelope)
    {
        envelope = default;
        if (!GridCellGeometry.TryCreatePrism(
                grid.Configuration.TopologyKind,
                grid.Configuration.TopologyMetrics,
                grid.BoundsMin,
                default,
                out GridCellPrism minimumPrism)
            || !GridCellGeometry.TryCreatePrism(
                grid.Configuration.TopologyKind,
                grid.Configuration.TopologyMetrics,
                grid.BoundsMax,
                default,
                out GridCellPrism maximumPrism))
        {
            return false;
        }

        TopologyVoxelAabb minimum = minimumPrism.GetAabb();
        TopologyVoxelAabb maximum = maximumPrism.GetAabb();
        envelope = new FixedBoundVolume(
            new Vector3d(
                FixedMath.Min(minimum.Min.X, maximum.Min.X),
                FixedMath.Min(minimum.Min.Y, maximum.Min.Y),
                FixedMath.Min(minimum.Min.Z, maximum.Min.Z)),
            new Vector3d(
                FixedMath.Max(minimum.Max.X, maximum.Max.X),
                FixedMath.Max(minimum.Max.Y, maximum.Max.Y),
                FixedMath.Max(minimum.Max.Z, maximum.Max.Z)));
        return true;
    }

    private void InsertBoundaryContactPair(ushort firstGridIndex, ushort secondGridIndex)
    {
        ushort source = firstGridIndex < secondGridIndex ? firstGridIndex : secondGridIndex;
        ushort target = firstGridIndex < secondGridIndex ? secondGridIndex : firstGridIndex;
        SwiftList<ushort> targets = GetOrCreateBoundaryContactRow(
            _boundaryContactTargetsBySource,
            source,
            out bool addedSourceRow);
        if (!InsertSortedUnique(targets, target))
            return;

        InsertSortedUnique(
            GetOrCreateBoundaryContactRow(_boundaryContactSourcesByTarget, target, out _),
            source);
        if (addedSourceRow)
            SetBoundaryContactSource(source);
    }

    private void RemoveBoundaryContactPairs(ushort gridIndex)
    {
        if (_boundaryContactTargetsBySource.TryGetValue(
                gridIndex,
                out SwiftList<ushort>? targets))
        {
            for (int i = 0; i < targets.Count; i++)
            {
                RemoveBoundaryContactIncident(
                    _boundaryContactSourcesByTarget,
                    targets[i],
                    gridIndex,
                    clearSourceBit: false);
            }

            _boundaryContactTargetsBySource.Remove(gridIndex);
            SwiftListPool<ushort>.Shared.Release(targets);
            ClearBoundaryContactSource(gridIndex);
        }

        if (_boundaryContactSourcesByTarget.TryGetValue(
                gridIndex,
                out SwiftList<ushort>? sources))
        {
            for (int i = 0; i < sources.Count; i++)
            {
                RemoveBoundaryContactIncident(
                    _boundaryContactTargetsBySource,
                    sources[i],
                    gridIndex,
                    clearSourceBit: true);
            }

            _boundaryContactSourcesByTarget.Remove(gridIndex);
            SwiftListPool<ushort>.Shared.Release(sources);
        }
    }

    private void RemoveBoundaryContactIncident(
        SwiftDictionary<ushort, SwiftList<ushort>> rows,
        ushort rowIndex,
        ushort incidentIndex,
        bool clearSourceBit)
    {
        if (!rows.TryGetValue(rowIndex, out SwiftList<ushort>? row)
            || !RemoveSorted(row, incidentIndex)
            || row.Count != 0)
        {
            return;
        }

        rows.Remove(rowIndex);
        SwiftListPool<ushort>.Shared.Release(row);
        if (clearSourceBit)
            ClearBoundaryContactSource(rowIndex);
    }

    private static SwiftList<ushort> GetOrCreateBoundaryContactRow(
        SwiftDictionary<ushort, SwiftList<ushort>> rows,
        ushort rowIndex,
        out bool added)
    {
        if (rows.TryGetValue(rowIndex, out SwiftList<ushort>? row))
        {
            added = false;
            return row;
        }

        row = SwiftListPool<ushort>.Shared.Rent();
        rows.Add(rowIndex, row);
        added = true;
        return row;
    }

    private static bool InsertSortedUnique(SwiftList<ushort> row, ushort value)
    {
        int index = FindSortedIndex(row, value);
        if (index < row.Count && row[index] == value)
            return false;

        row.Insert(index, value);
        return true;
    }

    private static bool RemoveSorted(SwiftList<ushort> row, ushort value)
    {
        int index = FindSortedIndex(row, value);
        if (index >= row.Count || row[index] != value)
            return false;

        row.RemoveAt(index);
        return true;
    }

    private static int FindSortedIndex(SwiftList<ushort> row, ushort value)
    {
        int minimum = 0;
        int maximum = row.Count;
        while (minimum < maximum)
        {
            int middle = minimum + ((maximum - minimum) >> 1);
            if (row[middle] < value)
                minimum = middle + 1;
            else
                maximum = middle;
        }

        return minimum;
    }

    private void SetBoundaryContactSource(ushort source)
    {
        _boundaryContactSourceWords ??= new ulong[BoundaryContactSourceWordCount];
        _boundaryContactSourceSummaryWords ??= new ulong[BoundaryContactSourceSummaryWordCount];
        int wordIndex = source >> 6;
        _boundaryContactSourceWords[wordIndex] |= 1UL << (source & 63);
        _boundaryContactSourceSummaryWords[wordIndex >> 6] |= 1UL << (wordIndex & 63);
        _boundaryContactSourceSummaryLength = Math.Max(
            _boundaryContactSourceSummaryLength,
            (wordIndex >> 6) + 1);
    }

    private void ClearBoundaryContactSource(ushort source)
    {
        if (_boundaryContactSourceWords == null || _boundaryContactSourceSummaryWords == null)
            return;

        int wordIndex = source >> 6;
        _boundaryContactSourceWords[wordIndex] &= ~(1UL << (source & 63));
        if (_boundaryContactSourceWords[wordIndex] == 0)
            _boundaryContactSourceSummaryWords[wordIndex >> 6] &= ~(1UL << (wordIndex & 63));

        while (_boundaryContactSourceSummaryLength > 0
            && _boundaryContactSourceSummaryWords[_boundaryContactSourceSummaryLength - 1] == 0)
        {
            _boundaryContactSourceSummaryLength--;
        }

    }

    private void ReleaseBoundaryContactPairs()
    {
        foreach (SwiftList<ushort> row in _boundaryContactTargetsBySource.Values)
            SwiftListPool<ushort>.Shared.Release(row);
        foreach (SwiftList<ushort> row in _boundaryContactSourcesByTarget.Values)
            SwiftListPool<ushort>.Shared.Release(row);

        _boundaryContactTargetsBySource.Clear();
        _boundaryContactSourcesByTarget.Clear();
        _boundaryContactSourceWords = null;
        _boundaryContactSourceSummaryWords = null;
        _boundaryContactSourceSummaryLength = 0;
    }

    private void UnlinkGridNeighbors(VoxelGrid gridToRemove)
    {
        if (!gridToRemove.IsConjoined)
            return;

        var neighborSets = gridToRemove.Neighbors!.DenseValues;
        int neighborSetCount = gridToRemove.Neighbors.Count;
        for (int neighborSetIndex = 0; neighborSetIndex < neighborSetCount; neighborSetIndex++)
        {
            foreach (int neighborIndex in neighborSets[neighborSetIndex])
            {
                VoxelGrid neighborGrid = ActiveGrids[neighborIndex];
                neighborGrid.TryRemoveGridNeighbor(gridToRemove);
            }
        }
    }

    internal bool CollectGridCandidates(
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftList<ushort> candidates,
        int candidateLimit)
    {
        SwiftThrowHelper.ThrowIfNegative(candidateLimit, nameof(candidateLimit));
        candidates.Clear();
        if (ActiveGrids.Count == 0)
            return true;

        FixedBoundVolume queryBounds = new(boundsMin, boundsMax);
        if (ActiveGrids.Count <= candidateLimit)
        {
            _spatialIndex.CollectCandidates(queryBounds, ActiveGrids, candidates);
            return true;
        }

        foreach (VoxelGrid grid in ActiveGrids)
        {
            FixedBoundVolume gridBounds = new(grid.BoundsMin, grid.BoundsMax);
            if (!gridBounds.Intersects(queryBounds))
                continue;
            if (candidates.Count >= candidateLimit)
                return false;

            candidates.Add(grid.GridIndex);
        }

        if (candidates.Count > 1)
            candidates.SortInPlace();
        return true;
    }

    private static FixedBoundVolume CreateExpandedBounds(
        Vector3d boundsMin,
        Vector3d boundsMax,
        Fixed64 padding)
    {
        Vector3d expansion = new(padding, padding, padding);
        return new FixedBoundVolume(boundsMin - expansion, boundsMax + expansion);
    }

    #region Lookup

    /// <summary>
    /// Retrieves a grid by its world-local index.
    /// </summary>
    /// <param name="index">The world-local grid slot to resolve.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <returns>True if the grid was resolved; otherwise false.</returns>
    public bool TryGetGrid(int index, out VoxelGrid? outGrid)
    {
        outGrid = null;
        if (!CanResolveGrid(index))
            return false;

        outGrid = ActiveGrids[index];
        return true;
    }

    /// <summary>
    /// Retrieves the grid containing a given world position.
    /// </summary>
    /// <param name="position">The world position to resolve.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <returns>True if a containing grid was found; otherwise false.</returns>
    public bool TryGetGrid(Vector3d position, out VoxelGrid? outGrid)
    {
        outGrid = null;
        if (!CanResolvePosition())
            return false;

        _spatialIndex.CollectPointCandidates(position, _gridCandidates);
        if (TryGetContainingGrid(position, _gridCandidates, out outGrid))
            return true;

        GridForgeLogger.Channel.Info($"No grid contains position {position}.");
        return false;
    }

    /// <summary>
    /// Retrieves the grid containing a 2D XZ-plane world position on the default world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <returns>True if a containing grid was found; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetGrid(Vector2d position, out VoxelGrid? outGrid) =>
         TryGetGrid(position, default, out outGrid);

    /// <summary>
    /// Retrieves the grid containing a 2D XZ-plane world position on the supplied world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve. Defaults to zero when omitted by paired overloads.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <returns>True if a containing grid was found; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetGrid(Vector2d position, Fixed64 layerY, out VoxelGrid? outGrid) =>
        TryGetGrid(GridPlane2d.ToWorld(position, layerY), out outGrid);

    /// <summary>
    /// Retrieves the active grid whose bounds are nearest to the supplied world position.
    /// </summary>
    /// <param name="position">The world position to resolve.</param>
    /// <param name="outGrid">The closest grid, if found.</param>
    /// <param name="topologyKind">Optional topology filter. When supplied, only grids using the requested topology are considered.</param>
    /// <returns>True if a closest active grid was resolved; otherwise false.</returns>
    public bool TryGetClosestGrid(
        Vector3d position,
        out VoxelGrid? outGrid,
        GridTopologyKind? topologyKind = null)
    {
        outGrid = null;
        if (!CanResolveActiveGrid())
            return false;

        Fixed64 closestDistanceSquared = Fixed64.MaxValue;
        foreach (VoxelGrid candidateGrid in ActiveGrids)
        {
            if (!candidateGrid.IsActive
                || !MatchesTopologyKind(candidateGrid, topologyKind))
            {
                continue;
            }

            Fixed64 distanceSquared = GetDistanceSquaredToBounds(position, candidateGrid.BoundsMin, candidateGrid.BoundsMax);
            if (outGrid == null || distanceSquared < closestDistanceSquared)
            {
                outGrid = candidateGrid;
                closestDistanceSquared = distanceSquared;
            }
        }

        return outGrid != null;
    }

    /// <summary>
    /// Retrieves the active grid whose bounds are nearest to a 2D XZ-plane world position on the default world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="outGrid">The closest grid, if found.</param>
    /// <param name="topologyKind">Optional topology filter. When supplied, only grids using the requested topology are considered.</param>
    /// <returns>True if a closest active grid was resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetClosestGrid(
        Vector2d position,
        out VoxelGrid? outGrid,
        GridTopologyKind? topologyKind = null) =>
        TryGetClosestGrid(position, default, out outGrid, topologyKind);

    /// <summary>
    /// Retrieves the active grid whose bounds are nearest to a 2D XZ-plane world position on the supplied world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve. Defaults to zero when omitted by paired overloads.</param>
    /// <param name="outGrid">The closest grid, if found.</param>
    /// <param name="topologyKind">Optional topology filter. When supplied, only grids using the requested topology are considered.</param>
    /// <returns>True if a closest active grid was resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetClosestGrid(
        Vector2d position,
        Fixed64 layerY,
        out VoxelGrid? outGrid,
        GridTopologyKind? topologyKind = null) =>
        TryGetClosestGrid(GridPlane2d.ToWorld(position, layerY), out outGrid, topologyKind);

    /// <summary>
    /// Retrieves a grid by a world-scoped voxel identity.
    /// </summary>
    /// <param name="worldVoxelIndex">The voxel identity whose grid should be resolved.</param>
    /// <param name="result">The resolved grid, if found.</param>
    /// <returns>True if the grid was resolved; otherwise false.</returns>
    public bool TryGetGrid(WorldVoxelIndex worldVoxelIndex, out VoxelGrid? result)
    {
        result = null;
        if (worldVoxelIndex.WorldSpawnToken != SpawnToken
            || !TryGetGrid(worldVoxelIndex.GridIndex, out VoxelGrid? resolvedGrid)
            || worldVoxelIndex.GridSpawnToken != resolvedGrid!.SpawnToken)
        {
            return false;
        }

        result = resolvedGrid;
        return true;
    }

    /// <summary>
    /// Retrieves the grid and voxel containing a given world position.
    /// </summary>
    /// <param name="position">The world position to resolve.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <param name="outVoxel">The resolved voxel, if found.</param>
    /// <returns>True if both the grid and voxel were resolved; otherwise false.</returns>
    public bool TryGetGridAndVoxel(
        Vector3d position,
        out VoxelGrid? outGrid,
        out Voxel? outVoxel)
    {
        outVoxel = null;
        return TryGetGrid(position, out outGrid)
            && outGrid!.TryGetVoxel(position, out outVoxel);
    }

    /// <summary>
    /// Retrieves the grid and voxel containing a 2D XZ-plane world position on the default world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <param name="outVoxel">The resolved voxel, if found.</param>
    /// <returns>True if both the grid and voxel were resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetGridAndVoxel(
        Vector2d position,
        out VoxelGrid? outGrid,
        out Voxel? outVoxel) =>
         TryGetGridAndVoxel(position, default, out outGrid, out outVoxel);

    /// <summary>
    /// Retrieves the grid and voxel containing a 2D XZ-plane world position on the supplied world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve. Defaults to zero when omitted by paired overloads.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <param name="outVoxel">The resolved voxel, if found.</param>
    /// <returns>True if both the grid and voxel were resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetGridAndVoxel(
        Vector2d position,
        Fixed64 layerY,
        out VoxelGrid? outGrid,
        out Voxel? outVoxel) =>
         TryGetGridAndVoxel(GridPlane2d.ToWorld(position, layerY), out outGrid, out outVoxel);

    /// <summary>
    /// Retrieves the physical voxel whose center is nearest to the supplied world position and the grid that owns it.
    /// Sparse grids only consider configured physical voxels.
    /// </summary>
    /// <param name="position">The world position to resolve.</param>
    /// <param name="outGrid">The grid that owns the closest physical voxel, if found.</param>
    /// <param name="outVoxel">The closest physical voxel, if found.</param>
    /// <param name="topologyKind">Optional topology filter. When supplied, only grids using the requested topology are considered.</param>
    /// <returns>True if a physical voxel was resolved; otherwise false.</returns>
    public bool TryGetClosestGridAndVoxel(
        Vector3d position,
        out VoxelGrid? outGrid,
        out Voxel? outVoxel,
        GridTopologyKind? topologyKind = null)
    {
        outGrid = null;
        outVoxel = null;
        if (!CanResolveActiveGrid())
            return false;

        Fixed64 closestDistanceSquared = Fixed64.MaxValue;
        if (TryGetClosestGrid(position, out VoxelGrid? closestBoundsGrid, topologyKind)
            && closestBoundsGrid!.ConfiguredVoxelCount != 0)
        {
            bool resolved = closestBoundsGrid.TryGetClosestVoxel(
                position,
                out outVoxel,
                out closestDistanceSquared);
            Debug.Assert(resolved);
            outGrid = closestBoundsGrid;
        }

        foreach (VoxelGrid candidateGrid in ActiveGrids)
        {
            if (candidateGrid == null
                || !candidateGrid.IsActive
                || candidateGrid.ConfiguredVoxelCount == 0
                || !MatchesTopologyKind(candidateGrid, topologyKind))
            {
                continue;
            }
            if (ReferenceEquals(candidateGrid, outGrid))
                continue;

            Fixed64 boundsDistanceSquared = GetDistanceSquaredToBounds(position, candidateGrid.BoundsMin, candidateGrid.BoundsMax);
            if (outVoxel != null && boundsDistanceSquared > closestDistanceSquared)
                continue;

            candidateGrid.TryGetClosestVoxel(
                position,
                out Voxel? candidateVoxel,
                out Fixed64 candidateDistanceSquared);

            if (IsBetterClosestVoxel(
                candidateDistanceSquared,
                candidateGrid,
                candidateVoxel!,
                closestDistanceSquared,
                outGrid,
                outVoxel))
            {
                outGrid = candidateGrid;
                outVoxel = candidateVoxel;
                closestDistanceSquared = candidateDistanceSquared;
            }
        }

        return outVoxel != null;
    }

    /// <summary>
    /// Retrieves the physical voxel whose center is nearest to a 2D XZ-plane world position on the default world Y layer and the grid that owns it.
    /// Sparse grids only consider configured physical voxels.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="outGrid">The grid that owns the closest physical voxel, if found.</param>
    /// <param name="outVoxel">The closest physical voxel, if found.</param>
    /// <param name="topologyKind">Optional topology filter. When supplied, only grids using the requested topology are considered.</param>
    /// <returns>True if a physical voxel was resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetClosestGridAndVoxel(
        Vector2d position,
        out VoxelGrid? outGrid,
        out Voxel? outVoxel,
        GridTopologyKind? topologyKind = null) =>
        TryGetClosestGridAndVoxel(position, default, out outGrid, out outVoxel, topologyKind);

    /// <summary>
    /// Retrieves the physical voxel whose center is nearest to a 2D XZ-plane world position on the supplied world Y layer and the grid that owns it.
    /// Sparse grids only consider configured physical voxels.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve. Defaults to zero when omitted by paired overloads.</param>
    /// <param name="outGrid">The grid that owns the closest physical voxel, if found.</param>
    /// <param name="outVoxel">The closest physical voxel, if found.</param>
    /// <param name="topologyKind">Optional topology filter. When supplied, only grids using the requested topology are considered.</param>
    /// <returns>True if a physical voxel was resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetClosestGridAndVoxel(
        Vector2d position,
        Fixed64 layerY,
        out VoxelGrid? outGrid,
        out Voxel? outVoxel,
        GridTopologyKind? topologyKind = null) =>
        TryGetClosestGridAndVoxel(GridPlane2d.ToWorld(position, layerY), out outGrid, out outVoxel, topologyKind);

    /// <summary>
    /// Retrieves the grid and voxel for a given voxel identity.
    /// </summary>
    /// <param name="worldVoxelIndex">The voxel identity to resolve.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <param name="result">The resolved voxel, if found.</param>
    /// <returns>True if both the grid and voxel were resolved; otherwise false.</returns>
    public bool TryGetGridAndVoxel(
        WorldVoxelIndex worldVoxelIndex,
        out VoxelGrid? outGrid,
        out Voxel? result)
    {
        result = null;
        return TryGetGrid(worldVoxelIndex, out outGrid)
            && outGrid!.TryGetVoxel(worldVoxelIndex.VoxelIndex, out result);
    }

    /// <summary>
    /// Retrieves a voxel from a world position.
    /// </summary>
    /// <param name="position">The world position to resolve.</param>
    /// <param name="result">The resolved voxel, if found.</param>
    /// <returns>True if the voxel was resolved; otherwise false.</returns>
    public bool TryGetVoxel(
        Vector3d position,
        out Voxel? result)
    {
        result = null;
        return TryGetGrid(position, out VoxelGrid? grid)
            && grid!.TryGetVoxel(position, out result);
    }

    /// <summary>
    /// Retrieves a voxel from a 2D XZ-plane world position on the default world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="result">The resolved voxel, if found.</param>
    /// <returns>True if the voxel was resolved; otherwise false.</returns>
    public bool TryGetVoxel(
        Vector2d position,
        out Voxel? result)
    {
        return TryGetVoxel(position, default, out result);
    }

    /// <summary>
    /// Retrieves a voxel from a 2D XZ-plane world position on the supplied world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve. Defaults to zero when omitted by paired overloads.</param>
    /// <param name="result">The resolved voxel, if found.</param>
    /// <returns>True if the voxel was resolved; otherwise false.</returns>
    public bool TryGetVoxel(
        Vector2d position,
        Fixed64 layerY,
        out Voxel? result)
    {
        return TryGetVoxel(GridPlane2d.ToWorld(position, layerY), out result);
    }

    /// <summary>
    /// Retrieves the physical voxel whose center is nearest to the supplied world position.
    /// Sparse grids only consider configured physical voxels.
    /// </summary>
    /// <param name="position">The world position to resolve.</param>
    /// <param name="result">The closest physical voxel, if found.</param>
    /// <param name="topologyKind">Optional topology filter. When supplied, only grids using the requested topology are considered.</param>
    /// <returns>True if a physical voxel was resolved; otherwise false.</returns>
    public bool TryGetClosestVoxel(
        Vector3d position,
        out Voxel? result,
        GridTopologyKind? topologyKind = null)
    {
        result = null;
        if (!TryGetClosestGridAndVoxel(position, out _, out Voxel? closestVoxel, topologyKind))
            return false;

        result = closestVoxel;
        return true;
    }

    /// <summary>
    /// Retrieves the physical voxel whose center is nearest to a 2D XZ-plane world position on the default world Y layer.
    /// Sparse grids only consider configured physical voxels.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="result">The closest physical voxel, if found.</param>
    /// <param name="topologyKind">Optional topology filter. When supplied, only grids using the requested topology are considered.</param>
    /// <returns>True if a physical voxel was resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetClosestVoxel(
        Vector2d position,
        out Voxel? result,
        GridTopologyKind? topologyKind = null) =>
        TryGetClosestVoxel(position, default, out result, topologyKind);

    /// <summary>
    /// Retrieves the physical voxel whose center is nearest to a 2D XZ-plane world position on the supplied world Y layer.
    /// Sparse grids only consider configured physical voxels.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve. Defaults to zero when omitted by paired overloads.</param>
    /// <param name="result">The closest physical voxel, if found.</param>
    /// <param name="topologyKind">Optional topology filter. When supplied, only grids using the requested topology are considered.</param>
    /// <returns>True if a physical voxel was resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetClosestVoxel(
        Vector2d position,
        Fixed64 layerY,
        out Voxel? result,
        GridTopologyKind? topologyKind = null) =>
        TryGetClosestVoxel(GridPlane2d.ToWorld(position, layerY), out result, topologyKind);

    /// <summary>
    /// Retrieves a voxel from a world-scoped voxel identity.
    /// </summary>
    /// <param name="worldVoxelIndex">The voxel identity to resolve.</param>
    /// <param name="result">The resolved voxel, if found.</param>
    /// <returns>True if the voxel was resolved; otherwise false.</returns>
    public bool TryGetVoxel(
        WorldVoxelIndex worldVoxelIndex,
        out Voxel? result)
    {
        result = null;
        return TryGetGrid(worldVoxelIndex, out VoxelGrid? grid)
            && grid!.TryGetVoxel(worldVoxelIndex.VoxelIndex, out result);
    }

    #endregion

    #region Internal Helpers

    /// <summary>
    /// Increments the version of the specified grid and optionally the world version.
    /// </summary>
    public void IncrementGridVersion(int index, bool significant = false)
    {
        if (!IsActive)
        {
            GridForgeLogger.Channel.Warn($"Grid world not active. Cannot increment grid versions.");
            return;
        }

        _gridLock.EnterWriteLock();
        try
        {
            if (significant)
                Version++;

            if (ActiveGrids.IsAllocated(index))
                ActiveGrids[index].IncrementVersion();
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Finds active grids in this world that overlap the supplied target grid.
    /// </summary>
    public IEnumerable<VoxelGrid> FindOverlappingGrids(VoxelGrid targetGrid)
    {
        SwiftList<VoxelGrid> overlappingGrids = new();
        FindOverlappingGridsInto(targetGrid, overlappingGrids);
        return overlappingGrids;
    }

    /// <summary>
    /// Clears and fills caller-owned storage with active grids that overlap the supplied target grid.
    /// </summary>
    /// <param name="targetGrid">The grid whose expanded topology bounds define the overlap query.</param>
    /// <param name="results">Caller-owned storage cleared and filled in ascending grid-slot order.</param>
    public void FindOverlappingGridsInto(VoxelGrid targetGrid, SwiftList<VoxelGrid> results)
    {
        SwiftThrowHelper.ThrowIfNull(targetGrid, nameof(targetGrid));
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));

        results.Clear();

        if (!IsActive)
        {
            GridForgeLogger.Channel.Warn($"Grid world not active. Cannot resolve overlaps.");
            return;
        }

        _spatialIndex.CollectCandidates(
            CreateExpandedBounds(
                targetGrid.BoundsMin,
                targetGrid.BoundsMax,
                targetGrid.Topology.OverlapTolerance),
            ActiveGrids,
            _gridCandidates);
        for (int candidateIndex = 0; candidateIndex < _gridCandidates.Count; candidateIndex++)
            TryAddOverlappingGrid(targetGrid, _gridCandidates[candidateIndex], results);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool MatchesTopologyKind(VoxelGrid grid, GridTopologyKind? topologyKind) =>
        !topologyKind.HasValue || grid.TopologyKind == topologyKind.Value;

    private static bool IsBetterClosestVoxel(
        Fixed64 candidateDistanceSquared,
        VoxelGrid candidateGrid,
        Voxel candidateVoxel,
        Fixed64 closestDistanceSquared,
        VoxelGrid? closestGrid,
        Voxel? closestVoxel)
    {
        if (closestVoxel == null || closestGrid == null)
            return true;

        if (candidateDistanceSquared != closestDistanceSquared)
            return candidateDistanceSquared < closestDistanceSquared;

        return candidateGrid.GridIndex < closestGrid.GridIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed64 GetDistanceSquaredToBounds(Vector3d position, Vector3d boundsMin, Vector3d boundsMax)
    {
        Fixed64 x = GetAxisDistanceToBounds(position.X, boundsMin.X, boundsMax.X);
        Fixed64 y = GetAxisDistanceToBounds(position.Y, boundsMin.Y, boundsMax.Y);
        Fixed64 z = GetAxisDistanceToBounds(position.Z, boundsMin.Z, boundsMax.Z);
        return x * x + y * y + z * z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed64 GetAxisDistanceToBounds(Fixed64 coordinate, Fixed64 min, Fixed64 max)
    {
        if (coordinate < min)
            return min - coordinate;

        return coordinate > max ? coordinate - max : Fixed64.Zero;
    }

    private bool CanResolveGrid(int index)
    {
        if (!CanResolveActiveGrid())
            return false;

        if (!IsGridIndexInActiveRange(index))
        {
            GridForgeLogger.Channel.Error($"GridIndex '{index}' is out-of-bounds for ActiveGrids.");
            return false;
        }

        return IsGridIndexAllocated(index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanResolveActiveGrid()
    {
        if (IsActive)
            return true;

        GridForgeLogger.Channel.Warn($"Grid world not active. Cannot resolve grids.");
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsGridIndexInActiveRange(int index) =>
         (uint)index < MaxGrids && (uint)index <= ActiveGrids.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsGridIndexAllocated(int index)
    {
        if (ActiveGrids.IsAllocated(index))
            return true;

        GridForgeLogger.Channel.Error($"GridIndex '{index}' has not been allocated to ActiveGrids.");
        return false;
    }

    private bool CanResolvePosition()
    {
        if (IsActive)
            return true;

        GridForgeLogger.Channel.Warn($"Grid world not active. Cannot resolve positions.");
        return false;
    }

    private bool TryGetContainingGrid(
        Vector3d position,
        SwiftList<ushort> gridList,
        out VoxelGrid? outGrid)
    {
        outGrid = null;

        for (int index = 0; index < gridList.Count; index++)
        {
            ushort candidateIndex = gridList[index];
            VoxelGrid candidateGrid = ActiveGrids[candidateIndex];
            if (candidateGrid.IsInBounds(position))
            {
                outGrid = candidateGrid;
                return true;
            }
        }

        return false;
    }

    private void TryAddOverlappingGrid(
        VoxelGrid targetGrid,
        ushort neighborIndex,
        SwiftList<VoxelGrid> overlappingGrids)
    {
        if (neighborIndex == targetGrid.GridIndex)
            return;

        overlappingGrids.Add(ActiveGrids[neighborIndex]);
    }

    internal void NotifyActiveGridChange(VoxelGrid? grid)
    {
        if (grid == null || !grid.IsActive)
            return;

        bool drainCommittedChanges;
        lock (ChangeSyncRoot)
        {
            GridEventInfo eventInfo = CreateGridEventInfo(
                grid,
                GridEventKind.GridChanged,
                AllocateChangeStamp());
            drainCommittedChanges = EnqueueCommittedChange(new GridCommittedChange(eventInfo));
        }

        if (drainCommittedChanges)
            DrainCommittedChanges();
    }

    internal void NotifyActiveGridChange(
        VoxelGrid? grid,
        GridEventKind changeKind,
        VoxelIndex voxelIndex,
        Vector3d affectedPosition)
    {
        if (grid == null || !grid.IsActive)
            return;

        bool drainCommittedChanges;
        lock (ChangeSyncRoot)
        {
            GridEventInfo eventInfo = CreateGridEventInfo(
                grid,
                changeKind,
                voxelIndex,
                affectedPosition,
                affectedPosition,
                AllocateChangeStamp(),
                hasVoxelState: true,
                isVoxelPresent: changeKind != GridEventKind.SparseVoxelRemoved,
                obstacleCount: 0);
            drainCommittedChanges = EnqueueCommittedChange(new GridCommittedChange(eventInfo));
        }

        if (drainCommittedChanges)
            DrainCommittedChanges();
    }

    #endregion

    #region Private Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ResolveSpatialGridCellSize(int spatialGridCellSize)
    {
        if (spatialGridCellSize <= 0)
        {
            GridForgeLogger.Channel.Warn($"Spatial grid cell size must be greater than zero. Falling back to default size {DefaultSpatialGridCellSize}.");
            return DefaultSpatialGridCellSize;
        }

        return spatialGridCellSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private GridEventInfo CreateGridEventInfo(
        VoxelGrid grid,
        GridEventKind changeKind,
        GridChangeStamp changeStamp)
    {
        grid.ChangeHighWaterSequence = changeStamp.Sequence;
        return new GridEventInfo(
            SpawnToken,
            grid.GridIndex,
            grid.SpawnToken,
            grid.Configuration,
            grid.Version,
            changeKind,
            default,
            grid.BoundsMin,
            grid.BoundsMax,
            changeStamp);
    }

    internal GridEventInfo CreateGridEventInfo(
        VoxelGrid grid,
        GridEventKind changeKind,
        VoxelIndex voxelIndex,
        Vector3d affectedBoundsMin,
        Vector3d affectedBoundsMax,
        GridChangeStamp changeStamp,
        bool hasVoxelState,
        bool isVoxelPresent,
        byte obstacleCount)
    {
        grid.ChangeHighWaterSequence = changeStamp.Sequence;
        return new GridEventInfo(
            SpawnToken,
            grid.GridIndex,
            grid.SpawnToken,
            grid.Configuration,
            grid.Version,
            changeKind,
            voxelIndex,
            affectedBoundsMin,
            affectedBoundsMax,
            changeStamp,
            hasVoxelState,
            isVoxelPresent,
            obstacleCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal GridChangeStamp AllocateChangeStamp()
    {
        Debug.Assert(Monitor.IsEntered(ChangeSyncRoot));
        if (_changeSequence == ulong.MaxValue)
            throw new InvalidOperationException("The GridWorld change sequence is exhausted.");

        _changeSequence++;
        return new GridChangeStamp(_changeSequence, _changeSequence);
    }

    internal bool EnqueueCommittedChange(GridCommittedChange change)
    {
        Debug.Assert(Monitor.IsEntered(ChangeSyncRoot));
        _committedChanges.Enqueue(change);
        if (_isPublishingCommittedChanges)
            return false;

        _isPublishingCommittedChanges = true;
        return true;
    }

    internal void DrainCommittedChanges()
    {
        Volatile.Write(ref _committedPublicationOwnerThreadId, Environment.CurrentManagedThreadId);
        try
        {
            while (true)
            {
                GridCommittedChange change;
                lock (ChangeSyncRoot)
                {
                    if (!_committedChanges.TryDequeue(out change))
                    {
                        _isPublishingCommittedChanges = false;
                        return;
                    }
                }

                GridObstacleManager.NotifyCommittedExact(change);
                switch (change.GridEvent.ChangeKind)
                {
                    case GridEventKind.GridAdded:
                        NotifyActiveGridAdded(change.GridEvent);
                        break;
                    case GridEventKind.GridRemoved:
                        NotifyActiveGridRemoved(change.GridEvent);
                        break;
                    case GridEventKind.WorldReset:
                        break;
                    default:
                        NotifyActiveGridChange(change.GridEvent);
                        break;
                }

                NotifyChangeCommitted(change.GridEvent);
                lock (ChangeSyncRoot)
                {
                    _publishedChangeSequence = change.GridEvent.ChangeSequence;
                    Monitor.PulseAll(ChangeSyncRoot);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _committedPublicationOwnerThreadId, 0);
        }
    }

    private void NotifyActiveGridAdded(GridEventInfo eventInfo)
    {
        Action<GridEventInfo>? handlers = _onActiveGridAdded;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<GridEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Channel.Error($"[Grid {eventInfo.GridIndex}] added notification error: {ex.Message}");
            }
        }
    }

    private void NotifyActiveGridRemoved(GridEventInfo eventInfo)
    {
        Action<GridEventInfo>? handlers = _onActiveGridRemoved;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<GridEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Channel.Error($"[Grid {eventInfo.GridIndex}] removed notification error: {ex.Message}");
            }
        }
    }

    internal void NotifyActiveGridChange(GridEventInfo eventInfo)
    {
        Action<GridEventInfo>? handlers = _onActiveGridChange;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<GridEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Channel.Error($"[Grid {eventInfo.GridIndex}] change notification error: {ex.Message}");
            }
        }
    }

    private void NotifyChangeCommitted(GridEventInfo eventInfo)
    {
        Action<GridEventInfo>? handlers = _onChangeCommitted;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<GridEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Channel.Error($"[Change {eventInfo.ChangeSequence}] committed notification error: {ex.Message}");
            }
        }
    }

    #endregion
}
