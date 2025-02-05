using FixedMathSharp;
using SwiftCollections;
using SwiftCollections.Pool;
using SwiftCollections.Dimensions;
using System;
using System.Collections.Generic;
using GridForge.Configuration;
using GridForge.Spatial;

namespace GridForge.Grids
{
    /// <summary>
    /// Represents a 3D grid structure for spatial organization, managing nodes and scan cells.
    /// Handles initialization, neighbor relationships, and occupancy tracking.
    /// </summary>
    public class Grid
    {
        #region Fields

        /// <summary>
        /// Grid width in number of nodes.
        /// </summary>
        private int _width;

        /// <summary>
        /// Grid height in number of nodes.
        /// </summary>
        private int _height;

        /// <summary>
        /// Grid length in number of nodes.
        /// </summary>
        private int _length;

        /// <summary>
        /// Total number of nodes within the grid.
        /// </summary>
        private int _size;

        /// <summary>
        /// Delegate for handling changes to boundary relationships with neighboring grids.
        /// </summary>
        public Action<GridChange, LinearDirection> OnBoundaryChange;

        private bool _disposed;

        #endregion

        #region Properties

        /// <summary>
        /// Unique token identifying the grid instance.
        /// </summary>
        public int SpawnToken { get; private set; }

        /// <summary>
        /// Global index of the grid within the world.
        /// </summary>
        public ushort GlobalIndex { get; private set; }

        /// <summary>
        /// Minimum and maximum bounds of the grid in world coordinates.
        /// </summary>
        public (Vector3d Min, Vector3d Max) Bounds { get; private set; }

        /// <summary>
        /// Center position of the grid in world space.
        /// </summary>
        public Vector3d BoundsCenter { get; private set; }

        /// <summary>
        /// The primary 3D collection of nodes managed by this grid.
        /// </summary>
        public Array3D<Node> Nodes { get; private set; }

        /// <inheritdoc cref="_width"/>
        public int Width => _width;

        /// <inheritdoc cref="_height"/>
        public int Height => _height;

        /// <inheritdoc cref="_length"/>
        public int Length => _length;

        /// <inheritdoc cref="_size"/>
        public int Size => _size;

        /// <summary>
        /// Stores the indices of neighboring grids.
        /// </summary>
        public byte[] Neighbors { get; private set; }

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
        public int ScanCellSize { get; private set; }

        /// <summary>
        /// Collection of scan cells indexed by their spatial hash key.
        /// </summary>
        public SwiftDictionary<int, ScanCell> ScanCells { get; private set; }

        /// <summary>
        /// Stores currently active (occupied) scan cells within the grid.
        /// </summary>
        public SwiftHashSet<int> ActiveScanCells { get; private set; }

        /// <summary>
        /// Total number of occupants in the grid.
        /// </summary>
        public uint GridOccupantCount { get; private set; }

        /// <summary>
        /// Indicates whether the grid is currently active.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Determines whether the grid is occupied (active and containing occupants).
        /// </summary>
        public bool IsOccupied => IsActive && GridOccupantCount > 0;

        /// <summary>
        /// Tracks the version of the grid, incremented when a <see cref="Node"/> is modified.
        /// </summary>
        public uint Version { get; private set; }

        #endregion

        #region Initialization & Reset

        /// <summary>
        /// Initializes the grid with the given settings.
        /// </summary>
        /// <param name="worldIndex">The unique index of this grid in the world.</param>
        /// <param name="configuration">The configuration settings for the grid.</param>
        internal void Initialize(ushort worldIndex, GridConfiguration configuration)
        {
            if (_disposed)
                return;

            Version = 1;

            GlobalIndex = worldIndex;

            Bounds = (configuration.GridMin, configuration.GridMax);
            BoundsCenter = configuration.GridCenter;

            ScanCellSize = configuration.ScanCellSize;

            SpawnToken = GetHashCode();

            _width = Math.Max(((Bounds.Max.x - Bounds.Min.x) / GlobalGridManager.NodeSize).CeilToInt(), 1);
            _height = Math.Max(((Bounds.Max.y - Bounds.Min.y) / GlobalGridManager.NodeSize).CeilToInt(), 1);
            _length = Math.Max(((Bounds.Max.z - Bounds.Min.z) / GlobalGridManager.NodeSize).CeilToInt(), 1);
            _size = _width * _height * _length;

            GenerateScanCells();
            GenerateGrid();

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
                Pools.ActiveScanCellPool.Release(ActiveScanCells);
                ActiveScanCells = null;
            }

            if (IsConjoined)
            {
                // Remove the reference to this grid from its neighbors
                for (byte i = 0; i < Neighbors.Length; i++)
                {
                    byte neighborIndex = Neighbors[i];
                    if (neighborIndex != byte.MaxValue && GlobalGridManager.GetGrid(neighborIndex, out Grid neighborGrid))
                        neighborGrid.RemoveGridNeighbor(this);
                    Neighbors[i] = byte.MaxValue;
                }
            }

            if (Neighbors != null)
            {
                Pools.GridNeighborPool.Release(Neighbors);
                Neighbors = null;
                NeighborCount = 0;
            }

            GridOccupantCount = 0;

            Bounds = default;
            BoundsCenter = default;

            SpawnToken = 0;
            Version = 0;

            GlobalIndex = ushort.MaxValue;

            _width = 0;
            _height = 0;
            _length = 0;
            _size = 0;

            IsActive = false;
        }

        #endregion

        #region Grid Construction

        /// <summary>
        /// Generates the scan cell overlay for the grid.
        /// </summary>
        private void GenerateScanCells()
        {
            int scanWidth = _width / ScanCellSize;
            int scanHeight = _height / ScanCellSize;
            int scanLength = _length / ScanCellSize;

            ScanCells = new SwiftDictionary<int, ScanCell>();

            for (int x = 0; x <= scanWidth; x++)
            {
                for (int y = 0; y <= scanHeight; y++)
                {
                    for (int z = 0; z <= scanLength; z++)
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
        private void GenerateGrid()
        {
#if DEBUG
            long startMem = GC.GetTotalMemory(true);
#endif

            Nodes = new Array3D<Node>(_width, _height, _length);

            for (int x = 0; x < _width; x++)
            {
                for (int y = 0; y < _height; y++)
                {
                    for (int z = 0; z < _length; z++)
                    {
                        Vector3d position = new Vector3d(
                                Bounds.Min.x + x * GlobalGridManager.NodeSize,
                                Bounds.Min.y + y * GlobalGridManager.NodeSize,
                                Bounds.Min.z + z * GlobalGridManager.NodeSize
                            );

                        // Skip if the node is already allocated (should not happen under normal conditions)
                        if (IsNodeAllocated(x, y, z))
                        {
                            Console.WriteLine($"Node at [ coordinate: {(x, y, z)} , position: {position} ] is already allocated.");
                            continue;
                        }

                        // Rent a node from the object pool and initialize it
                        Node node = Pools.NodePool.Rent();
                        var coordinates = new CoordinatesLocal(x, y, z);
                        node.Initialize(new CoordinatesGlobal(GlobalIndex, coordinates, SpawnToken), position, Version, IsOnBoundary(coordinates));
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

        #region Grid Modification

        /// <summary>
        /// Notifies the grid that a structural change has occurred.
        /// </summary>
        public void NotifyGridVersionChange() => Version++;

        /// <summary>
        /// Adds a neighboring grid and updates relationships.
        /// </summary>
        /// <param name="neighborGrid">The neighboring grid to add.</param>
        internal bool AddGridNeighbor(Grid neighborGrid)
        {
            // Determines the relative direction of the neighboring grid
            Vector3d centerDifference = neighborGrid.BoundsCenter - BoundsCenter;
            (int x, int y, int z) gridOffset = (centerDifference.x.Sign(), centerDifference.y.Sign(), centerDifference.z.Sign());

            // Convert the 3D offset to a directional index
            LinearDirection direction = GlobalGridManager.GetNeighborDirectionFromOffset(gridOffset);

            if (direction == LinearDirection.None)
                return false;

            // Ensure the neighbor array is allocated and store the new neighbor
            Neighbors ??= Pools.GridNeighborPool.Rent(GlobalGridManager.DirectionOffsets.Length);
            if (Neighbors[(int)direction] == byte.MaxValue) // only increase count if not updating
                NeighborCount++;

            Neighbors[(int)direction] = (byte)neighborGrid.GlobalIndex;

            Version++;

            // Notify grid nodes that a new neighbor has been added
            NotifyBoundaryChange(GridChange.AddGridNeighbor, direction);

            return true;
        }

        /// <summary>
        /// Removes a neighboring grid relationship.
        /// </summary>
        /// <param name="neighborGrid">The neighboring grid to remove.</param>
        internal bool RemoveGridNeighbor(Grid neighborGrid)
        {
            if (!IsConjoined)
                return false;

            Vector3d centerDifference = neighborGrid.BoundsCenter - BoundsCenter;
            (int x, int y, int z) gridOffset = (centerDifference.x.Sign(), centerDifference.y.Sign(), centerDifference.z.Sign());
            LinearDirection direction = GlobalGridManager.GetNeighborDirectionFromOffset(gridOffset);

            if (direction == LinearDirection.None || Neighbors[(int)direction] == byte.MaxValue)
                return false;

            Neighbors[(int)direction] = byte.MaxValue;

            if (--NeighborCount == 0)
            {
                Console.WriteLine($"Releasing unused neighbor collection.");
                Pools.GridNeighborPool.Release(Neighbors);
                Neighbors = null;
            }

            Version++;
            NotifyBoundaryChange(GridChange.RemoveGridNeighbor, direction); // Notify nodes of the removed neighbor

            return true;
        }

        /// <summary>
        /// Notifies the grid of a boundary change event.
        /// </summary>
        /// <param name="changeType">Type of change (added or removed).</param>
        /// <param name="direction">The direction of the affected boundary.</param>
        public void NotifyBoundaryChange(GridChange changeType, LinearDirection direction)
        {
            try
            {
                OnBoundaryChange?.Invoke(changeType, direction);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during boundary change notification: {ex.Message}");
            }
        }

        #endregion

        #region Grid Queries

        /// <summary>
        /// Determines if a node coordinate is at the boundary of the grid.
        /// </summary>
        public bool IsOnBoundary(CoordinatesLocal coord)
        {
            return coord.x == 0 || coord.x == Nodes.Width - 1
                || coord.y == 0 || coord.y == Nodes.Height - 1
                || coord.z == 0 || coord.z == Nodes.Length - 1;
        }

        /// <summary>
        /// Checks whether a given position falls within the grid bounds.
        /// </summary>
        public bool IsInBounds(Vector3d target)
        {
            return Bounds.Min.x <= target.x && target.x <= Bounds.Max.x
                && Bounds.Min.y <= target.y && target.y <= Bounds.Max.y
                && Bounds.Min.z <= target.z && target.z <= Bounds.Max.z;
        }

        /// <summary>
        /// Validates whether two grids overlap within a small tolerance.
        /// Ensures that grids don’t overlap beyond <see cref="GlobalGridManager.NodeResolution"/>.
        /// </summary>
        public bool IsGridOverlapValid(Grid other)
        {
            return Bounds.Max.x > other.Bounds.Min.x - GlobalGridManager.NodeResolution
                && Bounds.Min.x < other.Bounds.Max.x + GlobalGridManager.NodeResolution
                && Bounds.Max.y > other.Bounds.Min.y - GlobalGridManager.NodeResolution
                && Bounds.Min.y < other.Bounds.Max.y + GlobalGridManager.NodeResolution
                && Bounds.Max.z > other.Bounds.Min.z - GlobalGridManager.NodeResolution
                && Bounds.Min.z < other.Bounds.Max.z + GlobalGridManager.NodeResolution;
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
                Console.WriteLine($"The coordinate {(x, y, z)} is not valid for this grid.");

            return result;
        }

        /// <summary>
        /// Converts a world position to node coordinates within the grid.
        /// </summary>
        public bool GetNodeCoordinates(Vector3d position, out CoordinatesLocal outCoordinates)
        {
            outCoordinates = default;

            if (!IsActive)
            {
                Console.WriteLine($"This Grid is not currently allocated.");
                return false;
            }

            if (!IsInBounds(position))
            {
                Console.WriteLine($"Position does not fall in the bounds of this grid");
                return false;
            }

            // Convert world position to grid indices by subtracting the minimum bound
            // and dividing by the node size to get a zero-based index
            int x = (int)(position.x - Bounds.Min.x / GlobalGridManager.NodeSize);
            int y = (int)(position.y - Bounds.Min.y / GlobalGridManager.NodeSize);
            int z = (int)(position.z - Bounds.Min.z / GlobalGridManager.NodeSize);

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
        public bool GetNode(int x, int y, int z, out Node outGridNode)
        {
            outGridNode = null;

            if (!IsActive)
            {
                Console.WriteLine($"This Grid is not currently active.");
                return false;
            }

            if (!IsNodeAllocated(x, y, z))
            {
                Console.WriteLine($"Node at coorinate {(x, y, z)} is has not been allocated to the grid.");
                return false;
            }

            outGridNode = Nodes[x, y, z];
            return true;
        }

        /// <summary>
        /// Retrieves a grid node from a given coordinate.
        /// </summary>
        public bool GetNode(CoordinatesLocal coordinates, out Node outGridNode)
        {
            if (!GetNode(coordinates.x, coordinates.y, coordinates.z, out outGridNode))
                return false;

            return true;
        }

        /// <summary>
        /// Retrieve <see cref="Node"/> from world <see cref="Vector3d"/> points
        /// </summary>
        /// <returns>GridNode at the given position or null if the position is not valid.</returns>
        public bool GetNode(Vector3d position, out Node outGridNode)
        {
            outGridNode = null;

            if (!GetNodeCoordinates(position, out CoordinatesLocal coordinate))
            {
                Console.WriteLine($"Unable to locate coordinate at position {position}.");
                return false;
            }

            return GetNode(coordinate.x, coordinate.y, coordinate.z, out outGridNode);
        }

        /// <summary>
        /// Computes the scan cell key for a given world position.
        /// </summary>
        public int GetScanCellKey(Vector3d position)
        {
            if (!GetNodeCoordinates(position, out CoordinatesLocal nodeCoordinates))
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
                Console.WriteLine($"Position {coordinates} is not in the bounds for this grids Scan Cell overlay.");
                return -1;
            }

            return scanCellKey;
        }

        /// <summary>
        /// Retrieves a scan cell from the grid using its hashed key.
        /// </summary>
        public bool GetScanCell(int key, out ScanCell outScanCell)
        {
            if (!ScanCells.TryGetValue(key, out outScanCell))
            {
                Console.WriteLine($"Unable to locate Scan Cell for {key}.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Retrieves the scan cell corresponding to a given world position.
        /// </summary>
        public bool GetScanCell(Vector3d position, out ScanCell outScanCell)
        {
            outScanCell = null;
            int key = GetScanCellKey(position);
            if (key <= 0)
                return false;
            return GetScanCell(key, out outScanCell);
        }

        /// <summary>
        /// Retrieves the scan cell associated with the given node coordinates.
        /// </summary>
        public bool GetScanCell(CoordinatesLocal coordinates, out ScanCell outScanCell)
        {
            outScanCell = null;
            if (!GetNode(coordinates, out Node node))
                return false;
            return GetScanCell(node.ScanCellKey, out outScanCell);
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
        /// Marks a scan cell as active within the grid using its hashed key.
        /// </summary>
        internal bool RegisterActiveScanCell(int cellKey)
        {
            if (!ScanCells.ContainsKey(cellKey))
            {
                Console.WriteLine($"Unable to locate corresponding scan cell with Key: {cellKey}");
                return false;
            }

            ActiveScanCells ??= Pools.ActiveScanCellPool.Rent();
            if (!ActiveScanCells.Contains(cellKey))
                ActiveScanCells.Add(cellKey);

            GridOccupantCount++;
            return true;
        }

        /// <summary>
        /// Marks a scan cell as inactive when no longer in use.
        /// </summary>
        internal bool UnregisterActiveScanCell(int cellKey)
        {
            if (!ActiveScanCells.Contains(cellKey))
            {
                Console.WriteLine($"Scan Cell {cellKey} is not currently active.");
                return false;
            }

            ActiveScanCells.Remove(cellKey);

            if (--GridOccupantCount == 0)
            {
                Console.WriteLine($"Releasing unused active scan cells collection.");
                Pools.ActiveScanCellPool.Release(ActiveScanCells);
                ActiveScanCells = null;
            }

            return true;
        }

        public override int GetHashCode() => GlobalGridManager.GetSpawnHash(GlobalIndex, Bounds.Min.GetHashCode(), Bounds.Max.GetHashCode());

        #endregion
    }
}