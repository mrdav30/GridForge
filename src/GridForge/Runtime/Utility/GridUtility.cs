#if UNITY_EDITOR
using FixedMathSharp;
using UnityEngine;

namespace GridForge.Utility
{
    internal static partial class GridUtility
    {
        /// <summary>
        /// Converts a Unity Vector3 to a FixedMathSharp Vector3d.
        /// </summary>
        /// <param name="vec">The Unity Vector3 to convert.</param>
        /// <returns>A FixedMathSharp Vector3d with the corresponding components from the Unity Vector3.</returns>
        internal static Vector3d ToVector3d(this Vector3 vec)
        {
            return new Vector3d(vec.x, vec.y, vec.z);
        }

        /// <summary>
        /// Converts a FixedMathSharp Vector3d to a Unity Vector3.
        /// </summary>
        /// <param name="vec">The FixedMathSharp Vector3d to convert.</param>
        /// <returns>A Unity Vector3 with the corresponding components from the FixedMathSharp Vector3d.</returns>
        internal static Vector3 ToVector3(this Vector3d vec)
        {
            return new Vector3(vec.x.ToPreciseFloat(), vec.y.ToPreciseFloat(), vec.z.ToPreciseFloat());
        }
    }
}
#endif