#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Convenience commands if users prefer creating the demo scene in the Editor.</summary>
public static class DinoPadSceneTools
{
    [MenuItem("Tools/Dino Pad/Add Game To Current Scene")]
    public static void AddGameToCurrentScene()
    {
        DinoPadGame existing = Object.FindObjectOfType<DinoPadGame>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            return;
        }
        GameObject gameObject = new GameObject("Dino Pad Game");
        gameObject.AddComponent<DinoPadGame>();
        gameObject.AddComponent<DinoPadUnoSerial>();
        Undo.RegisterCreatedObjectUndo(gameObject, "Add Dino Pad Game");
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = gameObject;
    }

    [MenuItem("Tools/Dino Pad/Create New Demo Scene")]
    public static void CreateDemoScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject gameObject = new GameObject("Dino Pad Game");
        gameObject.AddComponent<DinoPadGame>();
        gameObject.AddComponent<DinoPadUnoSerial>();
        Camera camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.gameObject.tag = "MainCamera";
        camera.orthographic = true;
        camera.backgroundColor = Color.white;
        camera.clearFlags = CameraClearFlags.SolidColor;
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = gameObject;
        EditorSceneManager.SaveScene(scene, "Assets/DinoPadGame/Scenes/DinoPadGame.unity");
    }
}
#endif
