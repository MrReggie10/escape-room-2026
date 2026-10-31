using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Receives SUCCESS / FAILURE lines from the Arduino code checker via Ardity.
/// Put this on the same GameObject as Ardity's SerialController (e.g. "Captcha"),
/// with that GameObject in the SerialController's Message Listener slot.
/// Ardity calls OnMessageArrived / OnConnectionEvent on this component.
/// </summary>
[RequireComponent(typeof(SerialController))]
public class CodeResultListener : MonoBehaviour
{
    [Header("Events (same fields as before, so existing Inspector wiring is kept)")]
    public UnityEvent onSuccess;
    public UnityEvent onFailure;

    [Header("Options")]
    [Tooltip("Print every received line and each result to the Console.")]
    public bool logMessages = true;

    private SerialController controller;
    private bool sceneLoading;

    private void Awake()
    {
        controller = GetComponent<SerialController>();
        // Make sure Ardity delivers messages to this object.
        if (controller != null && controller.messageListener == null)
            controller.messageListener = gameObject;
    }

    // Called by Ardity for every line received from the Arduino.
    private void OnMessageArrived(string msg)
    {
        if (msg == null) return;
        msg = msg.Trim();
        if (logMessages) Debug.Log("Serial received: \"" + msg + "\"");

        if (msg.Equals("SUCCESS", System.StringComparison.OrdinalIgnoreCase))
        {
            if (logMessages) Debug.Log("Code correct!");
            onSuccess.Invoke();
        }
        else if (msg.Equals("FAILURE", System.StringComparison.OrdinalIgnoreCase))
        {
            if (logMessages) Debug.Log("Code incorrect.");
            onFailure.Invoke();
        }
        // anything else (CODE_SET, ERROR, ...) is ignored
    }

    // Called by Ardity when the port connects or disconnects.
    private void OnConnectionEvent(bool success)
    {
        Debug.Log(success ? "Arduino connected" : "Arduino disconnected / retrying");
    }

    /// <summary>
    /// Load a scene by name. Hook this up in the Inspector, e.g. On Success -> "Win".
    /// The scene must be in File > Build Settings > Scenes In Build.
    /// </summary>
    public void SwitchScene(string sceneName)
    {
        if (sceneLoading) return; // ignore repeat messages while loading
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("Scene \"" + sceneName + "\" is not in Build Settings (or the name is wrong).");
            return;
        }
        sceneLoading = true;
        SceneManager.LoadScene(sceneName);
    }

    /// <summary>Change the Arduino's secret code at runtime, e.g. SetCode("101100111").</summary>
    public void SetCode(string nineBits)
    {
        if (controller != null) controller.SendSerialMessage("SET " + nineBits);
    }
}