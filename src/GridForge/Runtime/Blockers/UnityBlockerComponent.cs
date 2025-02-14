#if UNITY_EDITOR
using FixedMathSharp;
using UnityEngine;

namespace GridForge.Blockers.Unity_Editor
{
    /// <summary>
    /// Available blocker types for Unity selection.
    /// </summary>
    public enum BlockerType
    {
        None = 0,
        Bounds = 1
    }

    /// <summary>
    /// Unity component that allows selection of a blocker type in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class UnityBlockerComponent : MonoBehaviour
    {
#pragma warning disable CS0649 // These fields are serialized via Unity Editor
        [SerializeField] private BlockerType _blockerType;

        [SerializeField] private bool _isActive;

        [SerializeField] private bool _cacheCoveredNodes;

        [SerializeField] private BoundingArea _manualBlockArea;
#pragma warning restore CS0649

        private IBlocker _blocker;

        public void Awake()
        {
            _blocker = BlockerFactory.CreateBlocker(_blockerType, this);
        }

        public void OnEnable()
        {
            _blocker?.ApplyBlockage();
        }

        public void OnDisable()
        {
            _blocker?.RemoveBlockage();
        }

        public bool IsActive => _isActive;
        public bool CacheCoveredNodes => _cacheCoveredNodes;
        public BoundingArea ManualBlockArea => _manualBlockArea;
    }

    public static class BlockerFactory
    {
        public static IBlocker CreateBlocker(BlockerType type, UnityBlockerComponent component)
        {
            return type switch
            {
                BlockerType.Bounds => new BoundsBlocker(
                    component.ManualBlockArea, 
                    component.IsActive, 
                    component.CacheCoveredNodes),
                _ => null
            };
        }
    }
}
#endif