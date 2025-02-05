#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace GridForge.Configuration.Unity_Editor
{
    /// <summary>
    /// Custom Unity Editor inspector for <see cref="GridConfigurationSaver"/>.
    /// Provides UI controls for managing multiple grid configurations in the scene.
    /// </summary>
    [CustomEditor(typeof(GridConfigurationSaver))]
    public class EditorGridConfigurationSaver : Editor
    {
        SerializedProperty _savedGridConfigurations;

        private const int minLimit = -100;
        private const int maxLimit = 100;

        public override void OnInspectorGUI()
        {
            GridConfigurationSaver gs = (GridConfigurationSaver)target;

            SerializedObject so = new SerializedObject(gs);
            GenerateProperties(so);

            EditorGUILayout.Space();

            // Display grid configurations
            EditorGUILayout.LabelField("Saved Grid Configurations", EditorStyles.boldLabel);

            for (int i = 0; i < _savedGridConfigurations.arraySize; i++)
            {
                SerializedProperty gridConfig = _savedGridConfigurations.GetArrayElementAtIndex(i);

                SerializedProperty xMin = gridConfig.FindPropertyRelative("GridMin.x");
                SerializedProperty xMax = gridConfig.FindPropertyRelative("GridMax.x");
                SerializedProperty heightMin = gridConfig.FindPropertyRelative("GridMin.y");
                SerializedProperty heightMax = gridConfig.FindPropertyRelative("GridMax.y");
                SerializedProperty zMin = gridConfig.FindPropertyRelative("GridMin.z");
                SerializedProperty zMax = gridConfig.FindPropertyRelative("GridMax.z");

                EditorGUILayout.LabelField($"Grid {i + 1}", EditorStyles.boldLabel);

                float xMinVal = xMin.floatValue;
                float xMaxVal = xMax.floatValue;
                EditorGUILayout.LabelField("X Min:", xMinVal.ToString());
                EditorGUILayout.LabelField("X Max:", xMaxVal.ToString());
                EditorGUILayout.MinMaxSlider(ref xMinVal, ref xMaxVal, minLimit, maxLimit);
                xMin.floatValue = xMinVal;
                xMax.floatValue = xMaxVal;

                float heightMinVal = heightMin.floatValue;
                float heightMaxVal = heightMax.floatValue;
                EditorGUILayout.LabelField("Height Min:", heightMinVal.ToString());
                EditorGUILayout.LabelField("Height Max:", heightMaxVal.ToString());
                EditorGUILayout.MinMaxSlider(ref heightMinVal, ref heightMaxVal, minLimit, maxLimit);
                heightMin.floatValue = heightMinVal;
                heightMax.floatValue = heightMaxVal;

                float zMinVal = zMin.floatValue;
                float zMaxVal = zMax.floatValue;
                EditorGUILayout.LabelField("Z Min:", zMinVal.ToString());
                EditorGUILayout.LabelField("Z Max:", zMaxVal.ToString());
                EditorGUILayout.MinMaxSlider(ref zMinVal, ref zMaxVal, minLimit, maxLimit);
                zMin.floatValue = zMinVal;
                zMax.floatValue = zMaxVal;

                if (GUILayout.Button("Remove Grid Configuration"))
                {
                    _savedGridConfigurations.DeleteArrayElementAtIndex(i);
                }

                EditorGUILayout.Space();
            }

            if (GUILayout.Button("Add New Grid Configuration"))
            {
                _savedGridConfigurations.InsertArrayElementAtIndex(_savedGridConfigurations.arraySize);
            }

            so.ApplyModifiedProperties();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.PropertyField(so.FindProperty("Show"));
                so.ApplyModifiedProperties();
            }

            EditorUtility.SetDirty(gs);
        }

        private void GenerateProperties(SerializedObject so)
        {
            _savedGridConfigurations = so.FindProperty("SavedGridConfigurations");
        }
    }
}
#endif