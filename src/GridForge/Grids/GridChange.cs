namespace GridForge.Grids
{
    /// <summary>
    /// Represents different types of grid-related changes that can occur.
    /// Used to track modifications such as adding/removing neighbors, obstacles, or occupants.
    /// </summary>
    public enum GridChange
    {
        /// <summary>
        /// Default, nothing happened
        /// </summary>
        None = -1,
        /// <summary>
        /// Signifies an add operation occured
        /// </summary>
        Add = 0,
        /// <summary>
        /// Signifies a remove operation occured
        /// </summary>
        Remove = 1,
        /// <summary>
        /// Signifies an update operation occured
        /// </summary>
        Update = 2
    }
}
