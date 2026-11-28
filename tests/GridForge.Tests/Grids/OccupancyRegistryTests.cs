using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using FixedMathSharp;
using GridForge.Spatial;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class OccupancyRegistryTests
{
    [Fact]
    public void CompetingPublication_ShouldReturnTheRegistryContainingTheWinningRegistration()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        TestOccupant occupant = new TestOccupant(Vector3d.Zero);
        Assert.True(grid.TryAddVoxelOccupant(voxel, occupant));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, occupant, voxel.WorldIndex, out OccupantTicket ticket));

        // Exercise the losing publisher deterministically rather than depending on
        // the operating system to pause a worker between its first read and CAS.
        WorldOccupancyRegistry candidate = new WorldOccupancyRegistry();
        MethodInfo publish = typeof(GridOccupantManager).GetMethod(
            "PublishWorldRegistry", BindingFlags.Static | BindingFlags.NonPublic);
        WorldOccupancyRegistry winner = (WorldOccupancyRegistry)publish.Invoke(null, new object[] { world, candidate });

        Assert.NotSame(candidate, winner);
        Assert.True(winner.Records.TryGetValue(occupant.GlobalId, out OccupancyRecord record));
        Assert.Same(occupant, record.Occupant);
        Assert.True(record.TryGetTicket(voxel.WorldIndex, out OccupantTicket recorded));
        Assert.Equal(ticket, recorded);
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, occupant, voxel.WorldIndex, out OccupantTicket currentTicket));
        Assert.Equal(ticket, currentTicket);
        Assert.True(grid.TryGetVoxelOccupant(voxel, currentTicket, out IVoxelOccupant resolved));
        Assert.Same(occupant, resolved);
        Assert.True(GridOccupantManager.TryDeregister(world, occupant));
        Assert.Equal(0, voxel.OccupantCount);
        Assert.Empty(winner.Records);
    }

    [Fact]
    public void ResetWorld_ShouldNotBeKeptAliveByPastOccupantRegistrations()
    {
        WeakReference<GridWorld> reference = CreateResetWorldWithPastOccupancy();
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            Assert.False(reference.TryGetTarget(out _));
        }
        finally
        {
            // Also clean up the original implementation when this regression fails.
            if (reference.TryGetTarget(out GridWorld retainedWorld))
                retainedWorld.Dispose();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public void SingleAndMultipleTicketsRetireOnLastRemovalResetAndDeactivation(int cells)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, new Vector3d(cells - 1, 0, 0));
        TestOccupant occupant = new TestOccupant(Vector3d.Zero);
        for (int i = 0; i < cells; i++)
            Assert.True(grid.TryAddVoxelOccupant(new VoxelIndex(i, 0, 0), occupant));
        WorldOccupancyRegistry registry = world.OccupancyRegistry;
        Assert.True(registry.Records.TryGetValue(occupant.GlobalId, out OccupancyRecord record));
        Assert.Equal(cells > 1, record.MultipleTickets != null);
        Assert.True(grid.TryRemoveVoxelOccupant(occupant));
        Assert.Empty(registry.Records);

        Assert.True(grid.TryAddVoxelOccupant(occupant));
        world.Reset();
        Assert.Empty(registry.Records);
        grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryAddVoxelOccupant(occupant));
        world.Reset(deactivate: true);
        Assert.Null(world.OccupancyRegistry);
        Assert.Empty(registry.Records);
    }

    [Fact]
    public void RemovingFromAnotherGridPreservesTheSingleRegistration()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid owner = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        VoxelGrid other = GridWorldTestFactory.AddGrid(world, Vector3d.One, Vector3d.One);
        TestOccupant occupant = new TestOccupant(Vector3d.Zero);
        Assert.True(owner.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        Assert.True(owner.TryAddVoxelOccupant(voxel, occupant));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, occupant, voxel.WorldIndex, out OccupantTicket before));

        Assert.False(other.TryRemoveVoxelOccupant(occupant));

        Assert.Equal(1, voxel.OccupantCount);
        Assert.Equal(voxel.WorldIndex, Assert.Single(GridOccupantManager.GetOccupiedIndices(world, occupant)));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, occupant, voxel.WorldIndex, out OccupantTicket after));
        Assert.Equal(before, after);
        Assert.True(owner.TryRemoveVoxelOccupant(voxel, occupant));
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(64, false)]
    public void OnlyOneSmallTicketMapIsReusedAndResetDropsIt(int cells, bool reusable)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, new Vector3d(cells - 1, 0, 0));
        TestOccupant first = new TestOccupant(Vector3d.Zero);
        TestOccupant second = new TestOccupant(Vector3d.Zero);
        for (int i = 0; i < cells; i++)
        {
            VoxelIndex index = new VoxelIndex(i, 0, 0);
            Assert.True(grid.TryAddVoxelOccupant(index, first));
            Assert.True(grid.TryAddVoxelOccupant(index, second));
        }
        WorldOccupancyRegistry registry = world.OccupancyRegistry;
        Assert.True(registry.Records.TryGetValue(first.GlobalId, out OccupancyRecord firstRecord));
        Assert.True(registry.Records.TryGetValue(second.GlobalId, out OccupancyRecord secondRecord));
        var firstMap = firstRecord.MultipleTickets;
        var secondMap = secondRecord.MultipleTickets;
        Assert.True(grid.TryRemoveVoxelOccupant(first));
        Assert.True(grid.TryRemoveVoxelOccupant(second));
        Assert.Null(firstRecord.MultipleTickets);
        Assert.Null(secondRecord.MultipleTickets);

        var nextMap = RegisterTwo();
        Assert.Equal(reusable, ReferenceEquals(firstMap, nextMap));
        Assert.NotSame(secondMap, nextMap);
        Assert.Equal(2, nextMap.Count);
        Assert.True(grid.TryRemoveVoxelOccupant(first));
        world.Reset();
        grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, new Vector3d(1, 0, 0));
        Assert.NotSame(nextMap, RegisterTwo());

        SwiftCollections.SwiftDictionary<WorldVoxelIndex, OccupantTicket> RegisterTwo()
        {
            Assert.True(grid.TryAddVoxelOccupant(new VoxelIndex(0, 0, 0), first));
            Assert.True(grid.TryAddVoxelOccupant(new VoxelIndex(1, 0, 0), first));
            Assert.True(registry.Records.TryGetValue(first.GlobalId, out OccupancyRecord record));
            Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));
            Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, first, voxel.WorldIndex, out OccupantTicket ticket));
            Assert.True(grid.TryGetVoxelOccupant(voxel, ticket, out IVoxelOccupant resolved));
            Assert.Same(first, resolved);
            return record.MultipleTickets;
        }
    }

    [Fact]
    public void ConcurrentFirstRegistrations_ShouldRetainBothGridsTickets()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid firstGrid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Vector3d secondPosition = new Vector3d(10, 0, 0);
        VoxelGrid secondGrid = GridWorldTestFactory.AddGrid(world, secondPosition, secondPosition);
        Assert.True(firstGrid.TryGetVoxel(Vector3d.Zero, out Voxel firstVoxel));
        Assert.True(secondGrid.TryGetVoxel(secondPosition, out Voxel secondVoxel));
        TestOccupant occupant = new TestOccupant(Vector3d.Zero);
        using CountdownEvent ready = new CountdownEvent(2);
        using ManualResetEventSlim start = new ManualResetEventSlim();
        bool[] added = new bool[2];
        Exception[] errors = new Exception[2];
        Thread first = CreateRegistrationThread(0, firstGrid, firstVoxel);
        Thread second = CreateRegistrationThread(1, secondGrid, secondVoxel);
        first.Start();
        second.Start();
        bool bothReady;
        bool firstJoined;
        bool secondJoined;
        try
        {
            bothReady = ready.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            start.Set();
            firstJoined = first.Join(TimeSpan.FromSeconds(10));
            secondJoined = second.Join(TimeSpan.FromSeconds(10));
        }

        Assert.True(bothReady);
        Assert.True(firstJoined);
        Assert.True(secondJoined);
        Assert.All(errors, error => Assert.Null(error));
        Assert.All(added, success => Assert.True(success));
        Assert.Equal(new[] { firstVoxel.WorldIndex, secondVoxel.WorldIndex },
            GridOccupantManager.GetOccupiedIndices(world, occupant));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, occupant, firstVoxel.WorldIndex, out OccupantTicket firstTicket));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, occupant, secondVoxel.WorldIndex, out OccupantTicket secondTicket));
        Assert.True(firstGrid.TryGetVoxelOccupant(firstVoxel, firstTicket, out IVoxelOccupant firstResolved));
        Assert.True(secondGrid.TryGetVoxelOccupant(secondVoxel, secondTicket, out IVoxelOccupant secondResolved));
        Assert.Same(occupant, firstResolved);
        Assert.Same(occupant, secondResolved);
        Assert.True(GridOccupantManager.TryDeregister(world, occupant));
        Assert.Equal(0, firstVoxel.OccupantCount);
        Assert.Equal(0, secondVoxel.OccupantCount);
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(world, occupant));

        Thread CreateRegistrationThread(int index, VoxelGrid grid, Voxel voxel)
        {
            return new Thread(() =>
            {
                try
                {
                    ready.Signal();
                    if (!start.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Registration start gate was not released.");
                    added[index] = grid.TryAddVoxelOccupant(voxel, occupant);
                }
                catch (Exception error)
                {
                    errors[index] = error;
                }
            })
            { IsBackground = true };
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reset_ShouldExposeOccupancyToCallbackThenRetireIt(bool deactivate)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        TestOccupant occupant = new TestOccupant(Vector3d.Zero);
        Assert.True(grid.TryAddVoxelOccupant(voxel, occupant));
        WorldVoxelIndex index = voxel.WorldIndex;
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, occupant, index, out OccupantTicket ticket));
        bool callbackFound = false;
        OccupantTicket callbackTicket = default;
        Action callback = () => callbackFound = GridOccupantManager.TryGetOccupancyTicket(world, occupant, index, out callbackTicket);
        world.OnReset += callback;
        world.Reset(deactivate);
        world.OnReset -= callback;

        Assert.True(callbackFound);
        Assert.Equal(ticket, callbackTicket);
        Assert.False(GridOccupantManager.TryGetOccupancyTicket(world, occupant, index, out _));
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(world, occupant));
        Assert.Equal(!deactivate, world.IsActive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<GridWorld> CreateResetWorldWithPastOccupancy()
    {
        GridWorld world = GridWorldTestFactory.CreateWorld();
        try
        {
            VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
            Assert.True(grid.TryAddVoxelOccupant(new TestOccupant(Vector3d.Zero)));
            world.Reset();
            // Return grids to their pools before deliberately dropping the last world owner.
            Assert.Empty(world.ActiveGrids);
            return new WeakReference<GridWorld>(world);
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }
}
