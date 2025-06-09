using Xunit;
using GridForge.Grids;
using FixedMathSharp;
using System.Linq;
using GridForge.Configuration;
using GridForge.Utility;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GridForge.Blockers.Tests
{
    [Collection("GridForgeCollection")] // Ensures shared GridForge state is reset per run
    public class BlockerTests
    {
        [Fact]
        public void Blocker_ShouldApplyBlockageToVoxels()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(30, 0, 30), new Vector3d(35, 0, 35)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(33, 0, 33);
            Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;
            Assert.NotNull(voxel);
            Assert.False(voxel.IsBlocked); // Ensure voxel is initially unblocked

            BoundingArea boundingArea = new BoundingArea(new Vector3d(32, 0, 32), new Vector3d(34, 0, 34));
            var blocker = new BoundsBlocker(boundingArea);
            blocker.ApplyBlockage();

            Assert.True(voxel.IsBlocked); // Voxel should now be blocked
        }

        [Fact]
        public void Blocker_ShouldRemoveBlockageFromVoxels()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(-35.5, 0, -35.5);
            Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;
            Assert.NotNull(voxel);

            BoundingArea boundingArea = new BoundingArea(new Vector3d(-36, 0, -36), new Vector3d(-35, 0, -35));
            var blocker = new BoundsBlocker(boundingArea);
            blocker.ApplyBlockage();
            Assert.True(voxel.IsBlocked);

            blocker.RemoveBlockage();
            Assert.False(voxel.IsBlocked);
        }

        [Fact]
        public void MultipleBlockers_ShouldStackCorrectly()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            var voxelSize = (float)GlobalGridManager.VoxelSize;

            Vector3d position = new Vector3d(-39 + voxelSize, 0, -39 + voxelSize);
            Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;
            Assert.NotNull(voxel);

            BoundingArea boundingArea1 = new BoundingArea(
                    new Vector3d(-40, 0, -40), 
                    new Vector3d(-39 + voxelSize, 0, -39 + voxelSize)
                );
            var blocker1 = new BoundsBlocker(boundingArea1);
            BoundingArea boundingArea2 = new BoundingArea(
                    new Vector3d(-39 + voxelSize, 0, -39 + voxelSize), 
                    new Vector3d(-39, 0, -39)
                );
            var blocker2 = new BoundsBlocker(boundingArea2);

            blocker1.ApplyBlockage();
            blocker2.ApplyBlockage();

            Assert.True(voxel.IsBlocked); // Ensure voxel is blocked
            Assert.True(voxel.ObstacleCount >= 2);
        }

        [Fact]
        public void RemovingOneBlocker_ShouldNotAffectOthers()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(
                new Vector3d(-40, 0, -40),
                new Vector3d(-30, 0, -30)),
                out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(-39.5, 0, -39.5);
            Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;
            Assert.NotNull(voxel);

            BoundingArea boundingArea1 = new BoundingArea(
                new Vector3d(-40, 0, -40),
                new Vector3d(-39.5, 0, -39.5));
            BoundingArea boundingArea2 = new BoundingArea(
                new Vector3d(-39.5, 0, -39.5),
                new Vector3d(-39, 0, -39));

            var blocker1 = new BoundsBlocker(boundingArea1);
            var blocker2 = new BoundsBlocker(boundingArea2);

            blocker1.ApplyBlockage();
            blocker2.ApplyBlockage();

            blocker1.RemoveBlockage();
            Assert.True(voxel.IsBlocked); // Should still be blocked because of blocker2
            Assert.True(voxel.ObstacleCount > 0);
        }

        [Fact]
        public void DeactivatingBlocker_ShouldPreventApplication()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(
                new Vector3d(-65, 0, -65),
                new Vector3d(-60, 0, -60)),
                out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(-60.5, 0, -60.5);
            Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;
            Assert.NotNull(voxel);

            BoundingArea boundingArea = new BoundingArea(new Vector3d(-61, 0, -61), new Vector3d(-60, 0, -60));
            var blocker = new BoundsBlocker(boundingArea, false);
            blocker.ApplyBlockage();

            Assert.False(voxel.IsBlocked); // Should not be blocked due to deactivation
        }

        [Fact]
        public void BoundsBlocker_ShouldAffectCorrectVoxels()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            var blockArea = new BoundingArea(new Vector3d(-40, 0, -40), new Vector3d(-39, 0, -39));
            var boundsBlocker = new BoundsBlocker(blockArea);
            boundsBlocker.ApplyBlockage();

            // Get all blocked voxels using GridTracer
            var blockedVoxels = GridTracer.GetCoveredVoxels(blockArea.Min, blockArea.Max)
                                         .SelectMany(covered => covered.Voxels) // Flatten the grouped voxels
                                         .ToList();

            Assert.NotEmpty(blockedVoxels);
            Assert.All(blockedVoxels, voxel => Assert.True(voxel.IsBlocked));
        }

        [Fact]
        public void Blocker_ShouldCorrectlyAffectEdgeVoxels()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            // Blocker placed at the grid's edge
            BoundingArea boundingArea = new BoundingArea(new Vector3d(-30, 0, -30), new Vector3d(-29.5, 0, -29.5));
            var blocker = new BoundsBlocker(boundingArea);
            blocker.ApplyBlockage();

            var blockedVoxels = GridTracer.GetCoveredVoxels(boundingArea.Min, boundingArea.Max)
                                         .SelectMany(covered => covered.Voxels)
                                         .ToList();

            Assert.NotEmpty(blockedVoxels);
            Assert.All(blockedVoxels, voxel => Assert.True(voxel.IsBlocked));
        }

        [Fact]
        public void Blocker_ShouldApplyAcrossMultipleGrids()
        {
            GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(-40, 0, -40),
                new Vector3d(-30, 0, -30)),
                out ushort grid1);
            GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(-30, 0, -30),
                new Vector3d(-20, 0, -20)),
                out ushort grid2);

            BoundingArea boundingArea = new BoundingArea(new Vector3d(-31, 0, -31), new Vector3d(-29, 0, -29));
            var blocker = new BoundsBlocker(boundingArea);
            blocker.ApplyBlockage();

            var blockedVoxels = GridTracer.GetCoveredVoxels(boundingArea.Min, boundingArea.Max)
                                         .SelectMany(covered => covered.Voxels)
                                         .ToList();

            Assert.NotEmpty(blockedVoxels);
            Assert.All(blockedVoxels, voxel => Assert.True(voxel.IsBlocked));
        }

        [Fact]
        public void Blockers_ShouldApplyToLocalGridInstance()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(
                new Vector3d(100, 0, 100),
                new Vector3d(150, 0, 150)),
                out _);

            List<BoundsBlocker> blockers = new List<BoundsBlocker>();

            for (int i = 0; i < 10; i++) // Keep it small for unit testing
            {
                Vector3d min = new Vector3d(100 + i, 0, 100 + i);
                Vector3d max = new Vector3d(101 + i, 0, 101 + i);
                var blocker = new BoundsBlocker(new BoundingArea(min, max));
                blockers.Add(blocker);
                blocker.ApplyBlockage(); // Modify local testGrid instead of GlobalGridManager
            }

            Assert.True(blockers.All(b => b.IsBlocking), "All blockers should have applied.");
        }

        [Fact]
        public void Blockers_ShouldBeThreadSafe()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-200, 0, -200), new Vector3d(-100, 0, -100)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Parallel.For(0, 100, i =>
            {
                Vector3d min = new Vector3d(-200 + i, 0, -200 + i);
                Vector3d max = new Vector3d(-199 + i, 0, -199 + i);
                var blocker = new BoundsBlocker(new BoundingArea(min, max));
                blocker.ApplyBlockage();
            });

            Assert.True(grid.ObstacleCount > 90); // Ensure most blockers applied correctly
        }
    }
}
