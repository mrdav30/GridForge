#if UNITY_EDITOR
using UnityEngine;
using FixedMathSharp;
using GridForge.Grids;

namespace GridForge.Utility.Debugging.Unity_Editor
{
    /// <summary>
    /// Defines types of nodes that can be visualized in the debugger.
    /// </summary>
    public enum NodeFilterType
    {
        All,        // Show all nodes
        Empty,      // Show only empty nodes
        Occupied,   // Show only occupied nodes
        Blocked     // Show only blocked nodes
    }

    /// <summary>
    /// Unity tool for visualizing grids and their nodes in the Scene View.
    /// This debugger highlights grid nodes, occupied spaces, and obstacles.
    /// </summary>
    [ExecuteAlways]
    public class GridDebugger : MonoBehaviour
    {
        #region Inspector Fields

        [Tooltip("Enable to visualize grid nodes.")]
        [SerializeField] private bool _showGrid = true;

        [Tooltip("Filter nodes based on their occupancy state.")]
        [SerializeField] private NodeFilterType _nodeFilter = NodeFilterType.All;

        [Tooltip("Enable to click on nodes to inspect them in Play Mode.")]
        [SerializeField] private bool _enableNodeSelection = true;

        [Tooltip("Color for the highlighted node.")]
        [SerializeField] private Color _highlightColor = Color.green;

        [Tooltip("The world position of the node to highlight.")]
        [SerializeField] private Vector3 _highlightedNodePosition;

        [Tooltip("Grid index to debug.")]
        [SerializeField] private ushort _gridIndex = 0;

        public ushort GridIndex
        {
            get => _gridIndex;
            set => _gridIndex = value;
        }

        private Grids.Grid _targetGrid;
        public bool EnableNodeSelection => _enableNodeSelection;
        public Node SelectedNode { get; private set; }

        #endregion

        #region Unity Lifecycle

        public void Update()
        {
            if (_enableNodeSelection && Application.isPlaying)
                HandleNodeSelection();
        }

        public void OnDrawGizmos()
        {
            if (!_showGrid || !Application.isPlaying)
                return;

            if (!GlobalGridManager.TryGetGrid(_gridIndex, out _targetGrid))
            {
                Debug.LogWarning($"Grid index {_gridIndex} is not available in GlobalGridManager.");
                return;
            }

            DrawGrid();
        }

        #endregion

        #region Node Selection Logic

        private void HandleNodeSelection()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                {
                    Vector3d hitPos = new Vector3d(hit.point.x, hit.point.y, hit.point.z);
                    if (GlobalGridManager.TryGetGridAndNode(hitPos, out Grids.Grid grid, out Node node))
                    {
                        _highlightedNodePosition = node.WorldPosition.ToVector3();

                        SelectedNode = node;
                        Debug.Log($"Node Selected: {node}");
                    }
                }
            }
        }

        #endregion

        #region Grid Visualization

        private void DrawGrid()
        {
            if (_targetGrid == null)
                return;

            int width = _targetGrid.Width;
            int height = _targetGrid.Height;
            int length = _targetGrid.Length;

            Gizmos.color = Color.magenta;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int z = 0; z < length; z++)
                    {
                        if (!_targetGrid.TryGetNode(x, y, z, out Node node) || !ShouldRenderNode(node))
                            continue;

                        DrawNodeGizmo(node);
                    }
                }
            }

            if (_highlightedNodePosition != Vector3.zero)
            {
                Gizmos.color = _highlightColor;
                Gizmos.DrawCube(_highlightedNodePosition, Vector3.one * 0.5f);
            }
        }

        private bool ShouldRenderNode(Node node)
        {
            return _nodeFilter switch
            {
                NodeFilterType.All => true,
                NodeFilterType.Empty => !node.IsOccupied && !node.IsBlocked,
                NodeFilterType.Occupied => node.IsOccupied,
                NodeFilterType.Blocked => node.IsBlocked,
                _ => true
            };
        }

        private void DrawNodeGizmo(Node node)
        {
            Vector3 nodePos = node.WorldPosition.ToVector3();
            Gizmos.color = node.IsBlocked ? Color.red : node.IsOccupied ? Color.yellow : Color.magenta;
            Gizmos.DrawCube(nodePos, Vector3.one * 0.4f);
        }

        #endregion
    }
}
#endif
