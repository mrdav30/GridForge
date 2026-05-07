using SwiftCollections;

namespace GridForge.Grids;

/// <summary>
/// Reusable temporary storage for allocation-sensitive grid scan operations.
/// </summary>
public sealed class GridScanScratch
{
    internal SwiftList<ScanCell> ScanCells { get; } = new();

    internal SwiftHashSet<ushort> ProcessedGrids { get; } = new();

    internal SwiftHashSet<ScanCell> ScanCellRedundancy { get; } = new();

    /// <summary>
    /// Clears all temporary scan state while retaining allocated backing storage.
    /// </summary>
    public void Clear()
    {
        ScanCells.Clear();
        ProcessedGrids.Clear();
        ScanCellRedundancy.Clear();
    }
}
