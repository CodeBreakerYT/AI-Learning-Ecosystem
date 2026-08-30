using UnityEditor;
using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Shared "Rebuild" button logic for the [ExecuteAlways] minigame scripts
    /// (LeversGame, ChemicalReactionLabGame,
    /// ChemistryMoleculeGame, PeriodicTableHuntGame). Each already builds its
    /// static geometry automatically the first time it's added to a scene -
    /// this button is for re-triggering that after a field/code tweak,
    /// without needing Play mode to see the result.
    /// </summary>
    internal static class MinigameRebuildEditorGUI
    {
        public static void DrawRebuildButton(Object target, System.Action rebuild)
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("Rebuild", GUILayout.Height(28)))
            {
                Undo.RegisterFullObjectHierarchyUndo(((Component)target).gameObject, "Rebuild Minigame");
                rebuild();
                EditorUtility.SetDirty(target);
                if (!Application.isPlaying)
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(((Component)target).gameObject.scene);
            }
        }
    }

    [CustomEditor(typeof(LeversGame))]
    public class LeversGameEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var config = (LeversGame)target;
            MinigameRebuildEditorGUI.DrawRebuildButton(config, config.Rebuild);
        }
    }

    [CustomEditor(typeof(ChemicalReactionLabGame))]
    public class ChemicalReactionLabGameEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var config = (ChemicalReactionLabGame)target;
            MinigameRebuildEditorGUI.DrawRebuildButton(config, config.Rebuild);
        }
    }

    [CustomEditor(typeof(ChemistryRealWorldGame))]
    public class ChemistryRealWorldGameEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var config = (ChemistryRealWorldGame)target;
            MinigameRebuildEditorGUI.DrawRebuildButton(config, config.Rebuild);
        }
    }

    [CustomEditor(typeof(ChemistryMoleculeGame))]
    public class ChemistryMoleculeGameEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var config = (ChemistryMoleculeGame)target;
            MinigameRebuildEditorGUI.DrawRebuildButton(config, config.Rebuild);
        }
    }

    [CustomEditor(typeof(PeriodicTableHuntGame))]
    public class PeriodicTableHuntGameEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var config = (PeriodicTableHuntGame)target;
            MinigameRebuildEditorGUI.DrawRebuildButton(config, config.Rebuild);
        }
    }

    [CustomEditor(typeof(MathCannonGame))]
    public class MathCannonGameEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var config = (MathCannonGame)target;
            MinigameRebuildEditorGUI.DrawRebuildButton(config, config.Rebuild);
        }
    }

    [CustomEditor(typeof(PhoboNewtonsGuide))]
    public class PhoboNewtonsGuideEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var config = (PhoboNewtonsGuide)target;
            MinigameRebuildEditorGUI.DrawRebuildButton(config, config.Rebuild);
        }
    }

    [CustomEditor(typeof(MinigameTeacher))]
    public class MinigameTeacherEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var config = (MinigameTeacher)target;
            MinigameRebuildEditorGUI.DrawRebuildButton(config, config.RebuildPreview);
        }
    }

    [CustomEditor(typeof(HubBootstrap))]
    public class HubBootstrapEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var config = (HubBootstrap)target;
            MinigameRebuildEditorGUI.DrawRebuildButton(config, config.Rebuild);
        }
    }
}
