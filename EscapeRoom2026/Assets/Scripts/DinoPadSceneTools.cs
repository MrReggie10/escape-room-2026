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
        GameObject gameObject = CreateGameObject();
        Undo.RegisterCreatedObjectUndo(gameObject, "Add Dino Pad Game");
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = gameObject;
    }

    [MenuItem("Tools/Dino Pad/Create New Demo Scene")]
    public static void CreateDemoScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject gameObject = CreateGameObject();
        Camera camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.gameObject.tag = "MainCamera";
        camera.orthographic = true;
        camera.backgroundColor = Color.white;
        camera.clearFlags = CameraClearFlags.SolidColor;
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = gameObject;
        EditorSceneManager.SaveScene(scene, "Assets/DinoPadGame/Scenes/DinoPadGame.unity");
    }

    /// <summary>Game + Ardity SerialController + listener, wired together.</summary>
    private static GameObject CreateGameObject()
    {
        GameObject gameObject = new GameObject("Dino Pad Game");
        DinoPadGame game = gameObject.AddComponent<DinoPadGame>();
        // RequireComponent adds the Ardity SerialController automatically.
        DinoPadUnoSerial uno = gameObject.AddComponent<DinoPadUnoSerial>();
        SerialController serial = gameObject.GetComponent<SerialController>();

        serial.baudRate = 115200;          // must match Serial.begin(115200) in the Uno sketch
        serial.messageListener = gameObject;
        serial.maxUnreadMessages = 32;     // default of 1 can drop quick presses
        // serial.portName must be set in the Inspector (e.g. COM3 or /dev/cu.usbmodem...).

        uno.serialController = serial;
        uno.game = game;
        return gameObject;
    }
}
#endif