#if UNITY_EDITOR
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Utility;
using System.Collections.Generic;
using UnityEngine;

namespace GridForge.Configuration.Unity_Editor
{
    /// <summary>
    /// Unity Editor utility for saving and applying grid configurations.
    /// Allows multiple grid configurations to be saved and applied in the scene editor.
    /// </summary>
    [ExecuteAlways] // Ensures it runs in edit mode
    public sealed class GridConfigurationSaver : MonoBehaviour
    {
        #region Properties

        /// <summary>
        /// List of saved grid configurations.
        /// </summary>
        public List<GridConfiguration> SavedGridConfigurations { get; private set; } = new List<GridConfiguration>();

        /// <summary>
        /// Enables visualization of grid bounds in the editor.
        /// </summary>
        public bool Show = true;

        #endregion

        #region Grid Management

        /// <summary>
        /// Saves the current grid configuration into the list.
        /// </summary>
        public void Save(Vector3d gridMin, Vector3d gridMax)
        {
            SavedGridConfigurations.Add(new GridConfiguration(gridMin, gridMax));
        }

        /// <summary>
        /// Applies all saved grid configurations to the global grid manager.
        /// </summary>
        public void EarlyApply()
        {
            foreach (var config in SavedGridConfigurations)
            {
                // Ensure grid bounds are valid before adding
                if (config.BoundsMax.x < config.BoundsMin.x
                    || config.BoundsMax.y < config.BoundsMin.y
                    || config.BoundsMax.z < config.BoundsMin.z)
                {
                    Debug.LogWarning("Invalid Grid Bounds: GridMax must be greater than or equal to GridMin.");
                    continue;
                }

                GlobalGridManager.TryAddGrid(config, out _);
            }
        }

        #endregion

        #region Editor Visualization

        public void OnDrawGizmos()
        {
            if (!Show || Application.isPlaying)
                return;

            Gizmos.color = Color.green;
            Vector3 scale = Vector3.one * 0.5f;

            foreach (var config in SavedGridConfigurations)
            {
                for (Fixed64 x = config.BoundsMin.x; x <= config.BoundsMax.x; x++)
                {
                    for (Fixed64 y = config.BoundsMin.y; y <= config.BoundsMax.y; y++)
                    {
                        for (Fixed64 z = config.BoundsMin.z; z <= config.BoundsMax.z; z++)
                        {
                            Vector3d drawPos = new Vector3d(x, y, z);
                            Gizmos.DrawCube(drawPos.ToVector3(), scale);
                        }
                    }
                }
            }
        }

        #endregion
    }
}
#endif