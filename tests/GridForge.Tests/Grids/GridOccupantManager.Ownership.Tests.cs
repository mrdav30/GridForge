using System;
using System.Threading;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridOccupantManagerOwnershipTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void DirectOccupantMutationRequiresTheReceiversPhysicalVoxel(bool remove, bool copyAddress, bool sameWorld)
    {
        using GridWorld a = GridWorldTestFactory.CreateWorld();
        using GridWorld other = GridWorldTestFactory.CreateWorld();
        GridWorld b = sameWorld ? a : other;
        VoxelGrid gridA = GridWorldTestFactory.AddGrid(a, Vector3d.Zero, Vector3d.Zero);
        Vector3d positionB = sameWorld ? Vector3d.One : Vector3d.Zero;
        VoxelGrid gridB = GridWorldTestFactory.AddGrid(b, positionB, positionB);
        Assert.True(gridA.TryGetVoxel(Vector3d.Zero, out Voxel voxelA));
        Assert.True(gridB.TryGetVoxel(positionB, out Voxel voxelB));
        TestOccupant owner = new TestOccupant(Vector3d.Zero);
        TestOccupant foreign = new TestOccupant(Vector3d.Zero);
        TestOccupant newcomer = new TestOccupant(Vector3d.Zero);
        Assert.True(gridA.TryAddVoxelOccupant(voxelA, owner));
        Assert.True(gridB.TryAddVoxelOccupant(voxelB, foreign));
        WorldVoxelIndex original = voxelB.WorldIndex;
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(a, owner, voxelA.WorldIndex, out OccupantTicket ticketA));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(b, foreign, original, out OccupantTicket ticketB));
        int notifications = 0;
        void Count(OccupantEventInfo _) => notifications++;
        GridOccupantManager.OnOccupantAdded += Count;
        GridOccupantManager.OnOccupantRemoved += Count;
        try
        {
            if (copyAddress)
                voxelB.WorldIndex = voxelA.WorldIndex;
            Assert.False(remove ? gridA.TryRemoveVoxelOccupant(voxelB, owner) : gridA.TryAddVoxelOccupant(voxelB, newcomer));
        }
        finally
        {
            voxelB.WorldIndex = original;
            GridOccupantManager.OnOccupantAdded -= Count;
            GridOccupantManager.OnOccupantRemoved -= Count;
        }
        Assert.Equal(0, notifications);
        Assert.Equal(1, voxelA.OccupantCount);
        Assert.Equal(1, voxelB.OccupantCount);
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(a, newcomer));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(a, owner, voxelA.WorldIndex, out OccupantTicket afterA));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(b, foreign, original, out OccupantTicket afterB));
        Assert.Equal(ticketA, afterA);
        Assert.Equal(ticketB, afterB);
        Assert.True(gridA.TryRemoveVoxelOccupant(voxelA, owner));
        Assert.True(gridB.TryRemoveVoxelOccupant(voxelB, foreign));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void NewVoxelLifetimeDoesNotReceiveThePreviousOccupantNotification(bool sameSparseAddress, bool add)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelIndex index = new VoxelIndex(0, 0, 0);
        GridConfiguration config = new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
            storageKind: sameSparseAddress ? GridStorageKind.Sparse : GridStorageKind.Dense);
        Assert.True(world.TryAddGrid(config, new[] { index }, out ushort slot));
        Assert.True(world.TryGetGrid(slot, out VoxelGrid grid));
        Assert.True(grid.TryGetVoxel(index, out Voxel oldVoxel));
        WorldVoxelIndex oldAddress = oldVoxel.WorldIndex;
        TestOccupant occupant = new TestOccupant(Vector3d.Zero);
        if (!add)
            Assert.True(grid.TryAddVoxelOccupant(oldVoxel, occupant));
        Voxel replacement = null;
        int staleCalls = 0;
        OccupantEventInfo captured = default;
        void Count(OccupantEventInfo _) => staleCalls++;
        void Replace(OccupantEventInfo change)
        {
            captured = change;
            if (sameSparseAddress)
            {
                if (!grid.TryRemoveVoxel(index) || !grid.TryAddVoxel(index, out replacement))
                    return;
            }
            else
            {
                if (!world.TryRemoveGrid(slot) || !world.TryAddGrid(config, out ushort newSlot)
                    || !world.TryGetGrid(newSlot, out VoxelGrid newGrid) || !newGrid.TryGetVoxel(index, out replacement))
                    return;
            }
            if (add)
                replacement.OnOccupantAdded += Count;
            else
                replacement.OnOccupantRemoved += Count;
        }
        if (add)
            GridOccupantManager.OnOccupantAdded += Replace;
        else
            GridOccupantManager.OnOccupantRemoved += Replace;
        try
        {
            Assert.True(add ? grid.TryAddVoxelOccupant(oldVoxel, occupant) : grid.TryRemoveVoxelOccupant(oldVoxel, occupant));
        }
        finally
        {
            GridOccupantManager.OnOccupantAdded -= Replace;
            GridOccupantManager.OnOccupantRemoved -= Replace;
        }
        Assert.Same(oldVoxel, replacement);
        Assert.Equal(sameSparseAddress, oldAddress == replacement.WorldIndex);
        Assert.Equal(oldAddress, captured.VoxelIndex);
        Assert.Same(occupant, captured.Occupant);
        Assert.True(captured.Ticket.IsValid);
        Assert.Equal(add ? 1 : 0, captured.OccupantCount);
        Assert.Equal(0, replacement.OccupantCount);
        Assert.Equal(0, staleCalls);
    }

    [Fact]
    public void PartitionTeardownRejectsDetachedOccupantMutationAndAllowsARegistrationOnALiveGrid()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid detached = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        VoxelGrid live = GridWorldTestFactory.AddGrid(world, Vector3d.One, Vector3d.One);
        Assert.True(detached.TryGetVoxel(Vector3d.Zero, out Voxel retiringVoxel));
        Assert.True(live.TryGetVoxel(Vector3d.One, out Voxel liveVoxel));
        TestOccupant occupant = new TestOccupant(Vector3d.One);
        bool? admittedRetiring = null;
        bool? admittedLive = null;
        Assert.True(retiringVoxel.TryAddPartition(new RemovalPartition(voxel =>
        {
            admittedRetiring = detached.TryAddVoxelOccupant(voxel, occupant);
            admittedLive = live.TryAddVoxelOccupant(liveVoxel, occupant);
        })));

        Assert.True(world.TryRemoveGrid(detached.GridIndex));

        Assert.False(admittedRetiring);
        Assert.True(admittedLive);
        Assert.Equal(liveVoxel.WorldIndex, Assert.Single(GridOccupantManager.GetOccupiedIndices(world, occupant)));
        Assert.True(live.TryRemoveVoxelOccupant(liveVoxel, occupant));
        Assert.False(detached.TryAddVoxelOccupant(retiringVoxel, occupant));
    }

    [Fact]
    public void OccupantIdentityAndLogCallbacksRunOutsideTheMutationLocks()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        using DiagnosticCaptureScope diagnostics = new DiagnosticCaptureScope();
        int logged = 0;
        int identityReads = 0;
        bool callbackInsideLock = false;
        void ObserveLocks() => callbackInsideLock |= Monitor.IsEntered(world.ChangeSyncRoot)
            || Monitor.IsEntered(grid.OccupantSyncRoot)
            || (world.OccupancyRegistry != null && Monitor.IsEntered(world.OccupancyRegistry.SyncRoot));
        Guid id = Guid.NewGuid();
        Guid ReadId()
        {
            identityReads++;
            ObserveLocks();
            return id;
        }
        GridForgeLogger.LogHandler = (_, _, _) => { logged++; ObserveLocks(); };
        ObservedOccupant occupant = new ObservedOccupant(ReadId);
        ObservedOccupant collision = new ObservedOccupant(ReadId);

        Assert.True(grid.TryAddVoxelOccupant(voxel, occupant));
        Assert.False(grid.TryAddVoxelOccupant(voxel, occupant));
        Assert.False(grid.TryAddVoxelOccupant(voxel, collision));
        Assert.False(grid.TryRemoveVoxelOccupant(voxel, collision));
        Assert.True(grid.TryRemoveVoxelOccupant(voxel, occupant));

        Assert.True(identityReads > 0);
        Assert.Equal(4, logged);
        Assert.False(callbackInsideLock);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BulkRemovalPreservesARegistrationReplacedDuringAnEarlierCallback(bool byWorld)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, new Vector3d(1, 0, 0));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel first));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel second));
        TestOccupant occupant = new TestOccupant(Vector3d.Zero);
        Assert.True(grid.TryAddVoxelOccupant(first, occupant));
        Assert.True(grid.TryAddVoxelOccupant(second, occupant));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, occupant, second.WorldIndex, out OccupantTicket original));
        bool replaced = false;
        void Replace(OccupantEventInfo change)
        {
            if (change.VoxelIndex == first.WorldIndex)
                replaced = grid.TryRemoveVoxelOccupant(second, occupant) && grid.TryAddVoxelOccupant(second, occupant);
        }
        first.OnOccupantRemoved += Replace;
        try
        {
            Assert.True(byWorld ? GridOccupantManager.TryDeregister(world, occupant) : grid.TryRemoveVoxelOccupant(occupant));
        }
        finally
        {
            first.OnOccupantRemoved -= Replace;
        }
        Assert.True(replaced);
        Assert.Equal(0, first.OccupantCount);
        Assert.Equal(1, second.OccupantCount);
        Assert.Equal(second.WorldIndex, Assert.Single(GridOccupantManager.GetOccupiedIndices(world, occupant)));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, occupant, second.WorldIndex, out OccupantTicket live));
        Assert.NotEqual(original, live);
        Assert.False(GridOccupantManager.ForgetTrackedOccupancy(world, occupant, second.WorldIndex, original));
        Assert.True(grid.TryRemoveVoxelOccupant(second, occupant));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void IdentityGetterCannotMoveAnInFlightAddOntoAReplacementVoxel(bool byAddress, bool sameSparseAddress)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelIndex index = new VoxelIndex(0, 0, 0);
        GridConfiguration config = new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
            storageKind: sameSparseAddress ? GridStorageKind.Sparse : GridStorageKind.Dense);
        Assert.True(world.TryAddGrid(config, new[] { index }, out ushort slot));
        Assert.True(world.TryGetGrid(slot, out VoxelGrid grid));
        Assert.True(grid.TryGetVoxel(index, out Voxel old));
        WorldVoxelIndex address = old.WorldIndex;
        Guid id = Guid.NewGuid();
        Voxel replacement = null;
        bool entered = false;
        ObservedOccupant occupant = new ObservedOccupant(() =>
        {
            if (!entered)
            {
                entered = true;
                if (sameSparseAddress)
                {
                    if (grid.TryRemoveVoxel(index))
                        grid.TryAddVoxel(index, out replacement);
                }
                else if (world.TryRemoveGrid(slot) && world.TryAddGrid(config, out ushort reused)
                    && world.TryGetGrid(reused, out VoxelGrid live))
                    live.TryGetVoxel(index, out replacement);
            }
            return id;
        });

        Assert.False(byAddress
            ? GridOccupantManager.TryAddVoxelOccupant(world, address, occupant)
            : grid.TryAddVoxelOccupant(old, occupant));
        Assert.Same(old, replacement);
        Assert.Equal(sameSparseAddress, address == replacement.WorldIndex);
        Assert.Equal(0, replacement.OccupantCount);
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(world, occupant));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GridPositionGetterCannotRegisterOnAReplacementGrid(bool sameWorld)
    {
        using GridWorld owner = GridWorldTestFactory.CreateWorld();
        using GridWorld other = GridWorldTestFactory.CreateWorld();
        GridWorld replacementOwner = sameWorld ? owner : other;
        VoxelGrid original = GridWorldTestFactory.AddGrid(owner, Vector3d.Zero, Vector3d.Zero);
        VoxelGrid replacement = null;
        Guid id = Guid.NewGuid();
        ObservedOccupant occupant = new ObservedOccupant(() => id, () =>
        {
            if (owner.TryRemoveGrid(original.GridIndex))
                replacement = GridWorldTestFactory.AddGrid(replacementOwner, Vector3d.Zero, Vector3d.Zero);
            return Vector3d.Zero;
        });

        Assert.False(original.TryAddVoxelOccupant(occupant));
        Assert.Same(original, replacement);
        Assert.False(replacement.IsOccupied);
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(replacementOwner, occupant));
    }

    [Fact]
    public void BulkIdentityGetterCannotDeregisterAReplacementGrid()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        Guid id = Guid.NewGuid();
        bool recycle = false;
        VoxelGrid replacement = null;
        ObservedOccupant occupant = null;
        occupant = new ObservedOccupant(() =>
        {
            if (recycle)
            {
                recycle = false;
                Assert.True(world.TryRemoveGrid(grid.GridIndex));
                replacement = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
                Assert.True(replacement.TryAddVoxelOccupant(occupant));
            }
            return id;
        });
        Assert.True(grid.TryAddVoxelOccupant(voxel, occupant));

        recycle = true;
        Assert.False(grid.TryRemoveVoxelOccupant(occupant));
        Assert.Same(grid, replacement);
        Assert.True(replacement.IsOccupied);
        Assert.True(replacement.TryRemoveVoxelOccupant(occupant));
    }

    [Fact]
    public void DirectMutationsRejectAMissingTargetVoxel()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        TestOccupant occupant = new TestOccupant(Vector3d.Zero);
        Assert.False(grid.TryAddVoxelOccupant((Voxel)null, occupant));
        Assert.False(grid.TryRemoveVoxelOccupant((Voxel)null, occupant));
        Assert.False(new VoxelGrid().TryAddVoxelOccupant(occupant));
    }

    private sealed class ObservedOccupant(Func<Guid> readId, Func<Vector3d> readPosition = null) : IVoxelOccupant
    {
        public Guid GlobalId => readId();
        public Vector3d Position => readPosition?.Invoke() ?? Vector3d.Zero;
        public byte OccupantGroupId => 0;
    }

    private sealed class RemovalPartition(Action<Voxel> onRemove) : IVoxelPartition
    {
        public WorldVoxelIndex WorldIndex { get; private set; }
        public void SetParentIndex(WorldVoxelIndex index) => WorldIndex = index;
        public void OnAddToVoxel(Voxel voxel) { }
        public void OnRemoveFromVoxel(Voxel voxel) => onRemove(voxel);
    }
}
