using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AILearningEcosystem.EditorTools
{
    /// <summary>
    /// The Lumora subject scenes ported into this project reference an original
    /// "Convai Essentials - XR" prefab whose source GUID no longer resolves (it was
    /// already a dangling reference in Lumora itself). This menu command drops a
    /// known-good instance of the real Convai Essentials - XR + demo NPC prefabs
    /// into the currently open scene instead of hand-patching that broken link.
    /// </summary>
    public static class AITutorSetup
    {
        private const string ConvaiEssentialsXRPath =
            "Assets/Convai/ConvaiXR/Prefabs/Convai Essentials - XR.prefab";

        private const string TutorNpcPath =
            "Assets/Convai/Demo/Avatars/Convai NPC Amelia.prefab";

        [MenuItem("Tools/AI Learning Ecosystem/Add AI Tutor To Open Scene")]
        private static void AddAiTutorToOpenScene()
        {
            var essentials = LoadPrefab(ConvaiEssentialsXRPath);
            var npc = LoadPrefab(TutorNpcPath);
            if (essentials == null || npc == null)
                return;

            var activeScene = EditorSceneManager.GetActiveScene();

            var essentialsInstance = InstantiateInScene(essentials, activeScene);
            var npcInstance = InstantiateInScene(npc, activeScene);
            npcInstance.transform.position = new Vector3(0f, 0f, 1.5f);

            Selection.objects = new Object[] { essentialsInstance, npcInstance };
            EditorSceneManager.MarkSceneDirty(activeScene);

            Debug.Log(
                $"Added '{essentials.name}' and '{npc.name}' to scene '{activeScene.name}'. " +
                "Position the NPC, then File > Save. Remember to set a real Convai API key " +
                "via the Convai setup window before entering Play Mode.",
                npcInstance);
        }

        private static GameObject LoadPrefab(string assetPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                Debug.LogError($"AITutorSetup: could not find prefab at '{assetPath}'.");
            return prefab;
        }

        private static GameObject InstantiateInScene(GameObject prefab, UnityEngine.SceneManagement.Scene scene)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, $"Add {prefab.name} to scene");
            return instance;
        }
    }
}
