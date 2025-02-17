using FixedMathSharp;
using SwiftCollections;
using SwiftCollections.Dimensions;
using System;
using System.Collections.Generic;
using GridForge.Configuration;
using GridForge.Spatial;
using SwiftCollections.Pool;

namespace GridForge.Grids
{
    /// <summary>
    /// Represents a 3D grid structure for spatial organization, managing nodes and scan cells.
    /// Handles initialization, neighbor relationships, and occupancy tracking.
    /// </summary>
    public class Grid
    {
        #region Properties

        /// <summary>
        /// Unique token identifying the grid instance.
        /// </summary>
        public int SpawnToken { get; private set; }

        /// <summary>
        /// Global index of the grid within the world.
        /// </summary>
        public ushort GlobalIndex { get; private set; }

        /// <inheritdoc cref="GridConfiguration"/>
        public GridConfiguration Configuration { get; private set; }

        /// <summary>
        /// Minimum bounds of the grid in world coordinates.
        /// </summary>
        public Vector3d BoundsMin => Configuration.BoundsMin;

        /// <summary>
        /// Maximum bounds of the grid in world coordinates.
        /// </summary>
        public Vector3d BoundsMax => Configuration.BoundsMax;

        /// <summary>
        /// Center position of the grid in world space.
        /// </summary>
        public Vector3d BoundsCenter => Configuration.GridCenter;

        /// <summary>
        /// Grid width in number of nodes.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Grid height in number of nodes.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Grid length in number of nodes.
        /// </summary>
        public int Length { get; private set; }

        /// <summary>
        /// Total number of nodes within the grid.
        /// </summary>
        public int Size { get; private set; }

        /// <summary>
        /// The primary 3D collection of nodes managed by this grid.
        /// </summary>
        public Array3D<Node> Nodes { get; private set; }

        /// <summary>
        /// Stores the indices of neighboring grids.
        /// </summary>
        /// <remarks>
        /// Unlike Node adjacency (which is always 1:1), grids can share multiple neighbors in the same direction.
        /// </remarks>
        public SwiftDictionary<LinearDirection, SwiftHashSet<int>> Neighbors { get; private set; }

        /// <summary>
        /// Count of currently linked neighboring grids.
        /// </summary>
        public byte NeighborCount { get; private set; }

        /// <summary>
        /// Determines whether this grid has any linked neighbors.
        /// </summary>
        public bool IsConjoined => Neighbors != null && NeighborCount > 0;

        /// <summary>
        /// Size of a scan cell used for spatial partitioning.
        /// </summary>
        public int ScanCellSize => Configuration.ScanCellSize;

        /// <summary>
        /// Collection of scan cells indexed by their spatial hash key.
        /// </summary>
        public SwiftDictionary<int, ScanCell> ScanCells { get; private set; }

        /// <summary>
        /// Stores currently active (occupied) scan cells within the grid.
        /// </summary>
        public SwiftHashSet<int> ActiveScanCells { get; internal set; }

        /// <summary>
        /// Indicates whether the grid is currently active.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Determines whether the grid is occupied (active and containing occupants).
        /// </summary>
        public bool IsOccupied => ActiveScanCells?.Count > 0;

        /// <summary>
        /// Tracks the number of obstacles currently registered in the grid.
        /// </summary>
        public int ObstacleCount { get; internal set; }

        /// <summary>
        /// Tracks the version of the grid, incremented when a <see cref="Node"/> is modified.
        /// </summary>
        public uint Version { get; internal set; }

        #endregion

        #region Initialization & Reset

        /// <summary>
        /// Initializes the grid with the given settings.
        /// </summary>
        /// <param name="globalIndex">The unique index of this grid in the world.</param>
        /// <param name="configuration">The configuration settings for the grid.</param>
        internal void Initialize(ushort globalIndex, GridConfiguration configuration)
        {
            if (IsActive)
            {
                GridForgeLogger.Warn($"Grid at {nameof(globalIndex)} is already active.");
                return;
            }

            Version = 1;

            GlobalIndex = globalIndex;

            Configuration = configuration;

            SpawnToken = GetHashCode();

            // +1 to account for inclusive bounds and to ensure that even the smallest grids (1x1x1) remain valid
            Width = ((BoundsMax.x - BoundsMin.x) / GlobalGridManager.NodeSize).FloorToInt() + 1;
            Height = ((BoundsMax.y - BoundsMin.y) / GlobalGridManager.NodeSize).FloorToInt() + 1;
            Length = ((BoundsMax.z - BoundsMin.z) / GlobalGridManager.NodeSize).FloorToInt() + 1;
            Size = Width * Height * Length;

            GenerateScanCells();
            GenerateNodes();

            IsActive = true;
        }

        /// <summary>
        /// Resets the grid, clearing all nodes and scan cells.
        /// </summary>
        internal void Reset()
        {
            if (!IsActive)
                return;

            if (Nodes != null)
            {
                foreach (Node node in Nodes)
                    Pools.NodePool.Release(node);
                Nodes = null;
            }

            if (ScanCells != null)
            {
                foreach (ScanCell cell in ScanCells.Values)
                    Pools.ScanCellPool.Release(cell);
                ScanCells = null;
            }

            if (ActiveScanCells != null)
            {
                SwiftCollectionPool<SwiftHashSet<int>, int>.Release(ActiveScanCells);
                ActiveScanCells = null;
            }

            if (Neighbors != null)
            {
                foreach (SwiftHashSet<int> neighbors in Neighbors.Values)
                    SwiftCollectionPool<SwiftHashSet<int>, int>.Release(neighbors);
                Neighbors = null;
                NeighborCount = 0;
            }

            Configuration = default;

            SpawnToken = 0;
            Version = 0;

            GlobalIndex = ushort.MaxValue;

            Width = 0;
            Height = 0;
            Length = 0;
            Size = 0;

            IsActive = false;
        }

        #endregion

        #region Grid Construction

        /// <summary>
        /// Generates the scan cell overlay for the grid.
        /// </summary>
        private void GenerateScanCells()
        {
            int scanWidth = ((Width - 1) / ScanCellSize) + 1;
            int scanHeight = ((Height - 1) / ScanCellSize) + 1;
            int scanLength = ((Length - 1) / ScanCellSize) + 1;

            ScanCells = new SwiftDictionary<int, ScanCell>();

            for (int x = 0; x < scanWidth; x++)
            {
                for (int y = 0; y < scanHeight; y++)
                {
                    for (int z = 0; z < scanLength; z++)
                    {
                        int cellKey = GlobalGridManager.GetSpawnHash(x, y, z);

                        ScanCell scanCell = Pools.ScanCellPool.Rent();
                        scanCell.Initialize(GlobalIndex, cellKey);
                        ScanCells.Add(cellKey, scanCell);
                    }
                }
            }
        }

        /// <summary>
        /// Generates the 3D grid structure based on the configured settings.
        /// </summary>
        private void GenerateNodes()
        {
#if DEBUG
            long startMem = GC.GetTotalMemory(true);
#endif

            Nodes = new Array3D<Node>(Width, Height, Length);

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int z = 0; z < Length; z++)
                    {
                        Vector3d position = new Vector3d(
                                BoundsMin.x + x * GlobalGridManager.NodeSize,
                                BoundsMin.y + y * GlobalGridManager.NodeSize,
                                BoundsMin.z + z * GlobalGridManager.NodeSize
                            );

                        // Skip if the node is already allocated (should not happen under normal conditions)
                        if (IsNodeAllocated(x, y, z))
                        {
                            GridForgeLogger.Warn(
                                $"Node at [ coordinate: {(x, y, z)} , position: {position} ] is already allocated.");
                            continue;
                        }

                        // Rent a node from the object pool and initialize it
                        Node node = Pools.NodePool.Rent();

                        CoordinatesLocal coordinates = new CoordinatesLocal(x, y, z);
                        bool isBoundaryNode = IsOnBoundary(coordinates);
                        int scanCellKey = GetScanCellKey(coordinates);

                        node.Initialize(
                            new CoordinatesGlobal(GlobalIndex, coordinates, SpawnToken),
                            position,
                            scanCellKey,
                            isBoundaryNode,
                            Version);

                        Nodes[x, y, z] = node;
                    }
                }
            }

#if DEBUG
            long usedMem = GC.GetTotalMemory(true) - startMem;
            Console.WriteLine($"Grid generated using {usedMem} Bytes.");
#endif
        }

        #endregion

        #region Boundary Management

        /// <summary>
        /// Determines the relative direction of a neighboring grid based on its center offset.
        /// </summary>
        /// <param name="a">The first grid.</param>
        /// <param name="b">The second grid.</param>
        /// <returns>The direction from grid 'a' to grid 'b'.</returns>
        public static LinearDirection GetNeighborDirection(Grid a, Grid b)
        {
            Vector3d centerDifference = b.BoundsCenter - a.BoundsCenter;
            (int x, int y, int z) gridOffset = (centerDifference.x.Sign(), centerDifference.y.Sign(), centerDifference.z.Sign());
            return GlobalGridManager.GetNeighborDirectionFromOffset(gridOffset);
        }

        /// <summary>
        /// Adds a neighboring grid and updates relationships.
        /// </summary>
        /// <param name="neighborGrid">The neighboring grid to add.</param>
        internal bool TryAddGridNeighbor(Grid neighborGrid)
        {
            LinearDirection neighborDirection = GetNeighborDirection(this, neighborGrid);

            if (neighborDirection == LinearDirection.None)
                return false;

            // Ensure the neighbor array is allocated and store the new neighbor
            Neighbors ??= new SwiftDictionary<LinearDirection, SwiftHashSet<int>>();
            if (!Neighbors.TryGetValue(neighborDirection, out SwiftHashSet<int> neighborSet))
            {
                neighborSet = SwiftCollectionPool<SwiftHashSet<int>, int>.Rent();
                Neighbors.Add(neighborDirection, neighborSet);
            }

            if (!neighborSet.Add(neighborGrid.GlobalIndex))
                return false;

            NeighborCount++;
            Version++;

            // Notify grid nodes that a new neighbor has been added
            NotifyBoundaryChange(neighborDirection);

            return true;
        }

        /// <summary>
        /// Removes a neighboring grid relationship.
        /// </summary>
        /// <param name="neighborGrid">The neighboring grid to remove.</param>
        internal bool TryRemoveGridNeighbor(Grid neighborGrid)
        {
            if (!IsConjoined)
                return false;

            LinearDirection neighborDirection = GetNeighborDirection(this, neighborGrid);
            if (neighborDirection == LinearDirection.None || !Neighbors.ContainsKey(neighborDirection))
                return false;

            if (!Neighbors[neighborDirection].Remove(neighborGrid.GlobalIndex))
                return false;

            if (Neighbors[neighborDirection].Count == 0)
            {
                GridForgeLogger.Info($"Releasing unused neighbor collection.");
                SwiftCollectionPool<SwiftHashSet<int>, int>.Release(Neighbors[neighborDirection]);
                Neighbors.Remove(neighborDirection);
            }

            if (--NeighborCount == 0)
                Neighbors = null;

            Version++;

            NotifyBoundaryChange(neighborDirection); // Notify nodes of the removed neighbor

            return true;
        }

        /// <summary>
        /// Notifies only the relevant boundary nodes when a neighboring grid is added or removed.
        /// Instead of looping through all nodes, it targets specific boundary rows or columns.
        /// </summary>
        /// <param name="direction">The direction of the affected boundary.</param>
        public void NotifyBoundaryChange(LinearDirection direction)
        {
            int boundaryX = direction switch
            {
                LinearDirection.West => 0,
                LinearDirection.East => Width - 1,
                _ => -1
            };

            int boundaryY = direction switch
            {
                LinearDirection.Below => 0,
                LinearDirection.Above => Height - 1,
                _ => -1
            };

            int boundaryZ = direction switch
            {
                LinearDirection.South => 0,
                LinearDirection.North => Length - 1,
                _ => -1
            };

            if (boundaryX != -1)
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int z = 0; z < Length; z++)
                        Nodes[boundaryX, y, z]?.InvalidateNeighborCache();
                }
            }
            else if (boundaryY != -1)
            {
                for (int x = 0; x < Width; x++)
                {
                    for (int z = 0; z < Length; z++)
                        Nodes[x, boundaryY, z]?.InvalidateNeighborCache();
                }
            }
            else if (boundaryZ != -1)
            {
                for (int x = 0; x < Width; x++)
                {
                    for (int y = 0; y < Height; y++)
                        Nodes[x, y, boundaryZ]?.InvalidateNeighborCache();
                }
            }
        }

        #endregion

        #region Grid Queries

        /// <summary>
        /// Determines if a node coordinate is at the boundary of the grid.
        /// Used to determine if a node should update when a neighboring grid is added/removed.
        /// </summary>
        public bool IsOnBoundary(CoordinatesLocal coord)
        {
            return coord.x == 0 || coord.x == Width - 1
                || coord.y == 0 || coord.y == Height - 1
                || coord.z == 0 || coord.z == Length - 1;
        }

        /// <summary>
        /// Checks whether a given position falls within the grid bounds.
        /// </summary>
        public bool IsInBounds(Vector3d target)
        {
            return BoundsMin.x <= target.x && target.x <= BoundsMax.x
                && BoundsMin.y <= target.y && target.y <= BoundsMax.y
                && BoundsMin.z <= target.z && target.z <= BoundsMax.z;
        }

        /// <summary>
        /// Checks if two grids are overlapping within a given tolerance threshold.
        /// This is used to determine if grids should be linked as neighbors.
        /// </summary>
        /// <param name="a">The first grid.</param>
        /// <param name="b">The second grid.</param>
        /// <param name="tolerance">Optional tolerance to account for minor floating-point errors.</param>
        /// <returns>True if the grids overlap within the tolerance, otherwise false.</returns>
        public static bool IsGridOverlapValid(Grid a, Grid b, Fixed64 tolerance = default)
        {
            tolerance = tolerance == default ? GlobalGridManager.NodeResolution : tolerance;

            return a.BoundsMax.x >= b.BoundsMin.x - tolerance
                && a.BoundsMin.x <= b.BoundsMax.x + tolerance
                && a.BoundsMax.y >= b.BoundsMin.y - tolerance
                && a.BoundsMin.y <= b.BoundsMax.y + tolerance
                && a.BoundsMax.z >= b.BoundsMin.z - tolerance
                && a.BoundsMin.z <= b.BoundsMax.z + tolerance;
        }

        /// <summary>
        /// Retrieves all neighboring grids connected to this grid.
        /// </summary>
        /// <returns>An enumeration of all neighboring grids.</returns>
        public IEnumerable<Grid> GetAllGridNeighbors()
        {
            if (!IsConjoined)
                yield break;

            foreach (SwiftHashSet<int> neighborSet in Neighbors.Values)
            {
                foreach (int neighborIndex in neighborSet)
                {
                    if (GlobalGridManager.TryGetGrid(neighborIndex, out Grid neighborGrid))
                        yield return neighborGrid;
                }
            }
        }

        /// <summary>
        /// Determines whether the given node coordinates are within the valid range of the grid.
        /// </summary>
        public bool IsValidNodeCoordinate(int x, int y, int z)
        {
            bool result = x >= 0 && x < Nodes.Width
                    && y >= 0 && y < Nodes.Height
                    && z >= 0 && z < Nodes.Length;

            if (!result)
                GridForgeLogger.Info($"The coordinate {(x, y, z)} is not valid for this grid.");

            return result;
        }

        /// <summary>
        /// Determines if a node is facing the boundary of the grid in a specific direction.
        /// Used to notify nodes when adjacent grids are added/removed.
        /// </summary>
        public bool IsFacingBoundaryDirection(CoordinatesLocal coordinates, LinearDirection direction)
        {
            return direction switch
            {
                // Principal directions (cardinal)
                LinearDirection.West => coordinates.x == 0,
                LinearDirection.East => coordinates.x == Width - 1,
                LinearDirection.North => coordinates.z == Length - 1,
                LinearDirection.South => coordinates.z == 0,
                LinearDirection.Above => coordinates.y == Height - 1,
                LinearDirection.Below => coordinates.y == 0,

                // Diagonal XY-plane
                LinearDirection.NorthWest => coordinates.x == 0 && coordinates.z == Length - 1,
                LinearDirection.NorthEast => coordinates.x == Width - 1 && coordinates.z == Length - 1,
                LinearDirection.SouthWest => coordinates.x == 0 && coordinates.z == 0,
                LinearDirection.SouthEast => coordinates.x == Width - 1 && coordinates.z == 0,

                // Diagonal XZ-plane (Above & Below variants)
                LinearDirection.AboveNorth => coordinates.y == Height - 1 && coordinates.z == Length - 1,
                LinearDirection.AboveSouth => coordinates.y == Height - 1 && coordinates.z == 0,
                LinearDirection.AboveWest => coordinates.y == Height - 1 && coordinates.x == 0,
                LinearDirection.AboveEast => coordinates.y == Height - 1 && coordinates.x == Width - 1,

                LinearDirection.BelowNorth => coordinates.y == 0 && coordinates.z == Length - 1,
                LinearDirection.BelowSouth => coordinates.y == 0 && coordinates.z == 0,
                LinearDirection.BelowWest => coordinates.y == 0 && coordinates.x == 0,
                LinearDirection.BelowEast => coordinates.y == 0 && coordinates.x == Width - 1,

                // Diagonal 3D Corners
                LinearDirection.AboveNorthWest => coordinates.x == 0
                    && coordinates.z == Length - 1
                    && coordinates.y == Height - 1,
                LinearDirection.AboveNorthEast => coordinates.x == Width - 1
                    && coordinates.z == Length - 1
                    && coordinates.y == Height - 1,
                LinearDirection.AboveSouthWest => coordinates.x == 0
                    && coordinates.z == 0
                    && coordinates.y == Height - 1,
                LinearDirection.AboveSouthEast => coordinates.x == Width - 1
                    && coordinates.z == 0
                    && coordinates.y == Height - 1,

                LinearDirection.BelowNorthWest => coordinates.x == 0
                    && coordinates.z == Length - 1
                    && coordinates.y == 0,
                LinearDirection.BelowNorthEast => coordinates.x == Width - 1
                    && coordinates.z == Length - 1
                    && coordinates.y == 0,
                LinearDirection.BelowSouthWest => coordinates.x == 0
                    && coordinates.z == 0
                    && coordinates.y == 0,
                LinearDirection.BelowSouthEast => coordinates.x == Width - 1
                    && coordinates.z == 0
                    && coordinates.y == 0,

                // No direction (should never be true)
                LinearDirection.None => false,

                // Catch-all (should never reach this)
                _ => false
            };
        }

        /// <summary>
        /// Converts a world position to node coordinates within the grid.
        /// </summary>
        public bool TryGetNodeCoordinates(Vector3d position, out CoordinatesLocal outCoordinates)
        {
            outCoordinates = default;

            if (!IsActive)
            {
                GridForgeLogger.Warn($"This Grid is not currently allocated.");
                return false;
            }

            if (!IsInBounds(position))
            {
                GridForgeLogger.Warn($"Position does not fall in the bounds of this grid");
                return false;
            }

            // Convert world position to grid indices by subtracting the minimum bound
            // and dividing by the node size to get a zero-based index
            (int x, int y, int z) = (
                ((position.x - BoundsMin.x) / GlobalGridManager.NodeSize).FloorToInt(),
                ((position.y - BoundsMin.y) / GlobalGridManager.NodeSize).FloorToInt(),
                ((position.z - BoundsMin.z) / GlobalGridManager.NodeSize).FloorToInt()
            );

            if (!IsValidNodeCoordinate(x, y, z))
                return false;

            outCoordinates = new CoordinatesLocal(x, y, z);
            return true;
        }

        /// <summary>
        /// Checks if a node at the given coordinates is allocated within the grid.
        /// </summary>
        public bool IsNodeAllocated(int x, int y, int z) =>
            IsValidNodeCoordinate(x, y, z)
            && Nodes[x, y, z] != null
            && Nodes[x, y, z].IsAllocated;

        /// <summary>
        /// Retrieves the <see cref="Node"/> at the specified coordinates, if allocated.
        /// </summary>
        public bool TryGetNode(int x, int y, int z, out Node outGridNode)
        {
            outGridNode = null;

            if (!IsActive)
            {
                GridForgeLogger.Warn($"This Grid is not currently active.");
                return false;
            }

            if (!IsNodeAllocated(x, y, z))
            {
                GridForgeLogger.Warn($"Node at coorinate {(x, y, z)} is has not been allocated to the grid.");
                return false;
            }

            outGridNode = Nodes[x, y, z];
            return true;
        }

        /// <summary>
        /// Retrieves a grid node from a given coordinate.
        /// </summary>
        public bool TryGetNode(CoordinatesLocal coordinates, out Node outGridNode)
        {
            return TryGetNode(coordinates.x, coordinates.y, coordinates.z, out outGridNode);
        }

        /// <summary>
        /// Retrieve <see cref="Node"/> from world <see cref="Vector3d"/> points
        /// </summary>
        /// <returns>GridNode at the given position or null if the position is not valid.</returns>
        public bool TryGetNode(Vector3d position, out Node outGridNode)
        {
            outGridNode = null;
            return TryGetNodeCoordinates(position, out CoordinatesLocal coordinate)
                && TryGetNode(coordinate.x, coordinate.y, coordinate.z, out outGridNode);
        }

        /// <summary>
        /// Computes the scan cell key for a given world position.
        /// </summary>
        public int GetScanCellKey(Vector3d position)
        {
            if (!TryGetNodeCoordinates(position, out CoordinatesLocal nodeCoordinates))
                return -1;

            return GetScanCellKey(nodeCoordinates);
        }

        /// <summary>
        /// Calculates the spatial cell index for a given position.
        /// </summary>
        public int GetScanCellKey(CoordinatesLocal coordinates)
        {
            (int x, int y, int z) = (
                    coordinates.x / ScanCellSize,
                    coordinates.y / ScanCellSize,
                    coordinates.z / ScanCellSize
                );

            int scanCellKey = GlobalGridManager.GetSpawnHash(x, y, z);
            if (!ScanCells.ContainsKey(scanCellKey))
            {
                GridForgeLogger.Warn($"Position {coordinates} is not in the bounds for this grids Scan Cell overlay.");
                return -1;
            }

            return scanCellKey;
        }

        /// <summary>
        /// Retrieves a scan cell from the grid using its hashed key.
        /// </summary>
        public bool TryGetScanCell(int key, out ScanCell outScanCell)
        {
            return ScanCells.TryGetValue(key, out outScanCell);
        }

        /// <summary>
        /// Retrieves the scan cell corresponding to a given world position.
        /// </summary>
        public bool TryGetScanCell(Vector3d position, out ScanCell outScanCell)
        {
            outScanCell = null;
            int key = GetScanCellKey(position);
            return TryGetScanCell(key, out outScanCell);
        }

        /// <summary>
        /// Retrieves the scan cell associated with the given node coordinates.
        /// </summary>
        public bool TryGetScanCell(CoordinatesLocal coordinates, out ScanCell outScanCell)
        {
            outScanCell = null;
            return TryGetNode(coordinates, out Node node)
                && TryGetScanCell(node.ScanCellKey, out outScanCell);
        }

        /// <summary>
        /// Enumerates all currently active scan cells within the grid.
        /// </summary>
        public IEnumerable<ScanCell> GetActiveScanCells()
        {
            if (!IsOccupied)
                yield break;

            foreach (int activeCellKey in ActiveScanCells)
            {
                if (ScanCells.TryGetValue(activeCellKey, out ScanCell scanCell))
                    yield return scanCell;
            }
        }

        /// <summary>
        /// Helper function to ceil snap a <see cref="Vector3d"/> to this grid's node size, ensuring it stays within grid bounds.
        /// </summary>
        public Vector3d CeilToGrid(Vector3d position)
        {
            Fixed64 nodeSize = GlobalGridManager.NodeSize;
            return new Vector3d(
                FixedMath.Clamp(((position.x - BoundsMin.x) / nodeSize).CeilToInt() * nodeSize + BoundsMin.x, BoundsMin.x, BoundsMax.x),
                FixedMath.Clamp(((position.y - BoundsMin.y) / nodeSize).CeilToInt() * nodeSize + BoundsMin.y, BoundsMin.y, BoundsMax.y),
                FixedMath.Clamp(((position.z - BoundsMin.z) / nodeSize).CeilToInt() * nodeSize + BoundsMin.z, BoundsMin.z, BoundsMax.z)
            );
        }

        /// <summary>
        /// Helper function to floor snap a <see cref="Vector3d"/> to this grid's node size, ensuring it stays within grid bounds.
        /// </summary>
        public Vector3d FloorToGrid(Vector3d position)
        {
            Fixed64 nodeSize = GlobalGridManager.NodeSize;
            return new Vector3d(
                FixedMath.Clamp(((position.x - BoundsMin.x) / nodeSize).FloorToInt() * nodeSize + BoundsMin.x, BoundsMin.x, BoundsMax.x),
                FixedMath.Clamp(((position.y - BoundsMin.y) / nodeSize).FloorToInt() * nodeSize + BoundsMin.y, BoundsMin.y, BoundsMax.y),
                FixedMath.Clamp(((position.z - BoundsMin.z) / nodeSize).FloorToInt() * nodeSize + BoundsMin.z, BoundsMin.z, BoundsMax.z)
            );
        }


        public override int GetHashCode() =>
            GlobalGridManager.GetSpawnHash(GlobalIndex, BoundsMin.GetHashCode(), BoundsMax.GetHashCode());

        #endregion
    }
}