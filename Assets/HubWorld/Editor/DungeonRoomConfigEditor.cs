using UnityEditor;
using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Adds a "Rebuild Dungeon" button so the room can be re-tiled in the
    /// Scene view after tweaking moduleSize/roomWidthModules/etc, without
    /// needing to enter Play Mode to see the result (DungeonRoomConfig
    /// already builds automatically once via [ExecuteAlways] Awake() - this
    /// is for re-triggering it after a field edit, since auto-rebuilding on
    /// every single field change would be sluggish while dragging a slider).
    /// </summary>
    [CustomEditor(typeof(DungeonRoomConfig))]
    public class DungeonRoomConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var config = (DungeonRoomConfig)target;
            EditorGUILayout.Space();
            if (GUILayout.Button("Rebuild Dungeon", GUILayout.Height(28)))
            {
                Undo.RegisterFullObjectHierarchyUndo(config.gameObject, "Rebuild Dungeon");
                config.Rebuild();
                EditorUtility.SetDirty(config);
                if (!Application.isPlaying)
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(config.gameObject.scene);
            }
        }
    }
}
