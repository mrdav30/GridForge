namespace GridForge.Blockers
{
    /// <summary>
    /// Defines the interface for a grid blocker, allowing custom implementations.
    /// </summary>
    public interface IBlocker
    {
        /// <summary>
        /// Called to initialize the blocker and apply obstacles to the grid.
        /// </summary>
        void ApplyBlockage();

        /// <summary>
        /// Called to remove the blockage from the grid when necessary.
        /// </summary>
        void RemoveBlockage();
    }
}
