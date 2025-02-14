#if UNITY_EDITOR
using GridForge.Grids;
using System.Linq;
using UnityEditor;

namespace GridForge.Utility.Debugging.Unity_Editor
{
    /// <summary>
    /// Custom Unity Editor inspector for <see cref="GridDebugger"/>.
    /// Displays detailed node metadata in the Inspector when a node is selected.
    /// </summary>
    [CustomEditor(typeof(GridDebugger))]
    public class EditorGridDebugger : Editor
    {
        private GridDebugger _debugger;
        private ushort[] _availableGridIndexes = new ushort[0];
        private string[] _gridIndexLabels = new string[0];

        public void OnEnable()
        {
            _debugger = (GridDebugger)target;
            UpdateGridIndexes();
        }

        private void UpdateGridIndexes()
        {
            _availableGridIndexes = new ushort[GlobalGridManager.ActiveGrids.Count];
            int count = 0;
            foreach (Grid grid in GlobalGridManager.ActiveGrids)
            {
                _availableGridIndexes[count] = grid.GlobalIndex;
                count++;
            }

            _gridIndexLabels = _availableGridIndexes.Select(index => $"Grid {index}").ToArray();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Grid Debugger Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_showGrid"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_nodeFilter"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Grid Selection", EditorStyles.boldLabel);

            if (_availableGridIndexes.Length > 0)
            {
                ushort selectedGridIndex = (ushort)EditorGUILayout.Popup(
                    "Active Grid",
                    System.Array.IndexOf(_availableGridIndexes, _debugger.GridIndex),
                    _gridIndexLabels);
                if (selectedGridIndex >= 0)
                    _debugger.GridIndex = _availableGridIndexes[selectedGridIndex];
            }
            else
            {
                EditorGUILayout.HelpBox("No active grids found.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Node Selection", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_enableNodeSelection"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_highlightColor"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Select Node", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_highlightedNodePosition"));

            if (_debugger.EnableNodeSelection)
            {
                if (_debugger.SelectedNode != null)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Selected Node Information", EditorStyles.boldLabel);
                    EditorGUILayout.Vector3Field("World Position", _debugger.SelectedNode.WorldPosition.ToVector3());
                    EditorGUILayout.Toggle("Occupied", _debugger.SelectedNode.IsOccupied);
                    EditorGUILayout.Toggle("Blocked", _debugger.SelectedNode.IsBlocked);
                    EditorGUILayout.LabelField("Spawn Token", _debugger.SelectedNode.SpawnToken.ToString());
                }
                else
                {
                    EditorGUILayout.HelpBox("No node selected. Click on a node in the Scene View while in Play Mode, or enter a position.", MessageType.Info);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
