namespace GridForge.Grids
{
    /// <summary>
    /// Represents the result of adding a grid to 
    /// <see cref="GlobalGridManager.TryAddGrid(Configuration.GridConfiguration, out ushort)"/>
    /// </summary>
    public enum GridAddResult
    {
        /// <summary>
        /// Grid was successfully added.
        /// </summary>
        Success = 0,
        /// <summary>
        /// A grid with the same bounds already exists.
        /// </summary>
        AlreadyExists = 1,
        /// <summary>
        /// The provided bounds are invalid.
        /// </summary>
        InvalidBounds = 2,
        /// <summary>
        /// The maximum number of grids has been reached.
        /// </summary>
        MaxGridsReached = 3,
        /// <summary>
        /// The <see cref="GlobalGridManager"/> has not been setup yet.
        /// </summary>
        InActive = 4
    }
}
