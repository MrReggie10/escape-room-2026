using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Attach to any GameObject, then drag that GameObject into the
/// "Message Listener" slot of Ardity's SerialController.
/// Ardity calls OnMessageArrived / OnConnectionEvent via SendMessage.
/// </summary>
public class CodeResultListener : MonoBehaviour
{
    public UnityEvent onSuccess;
    public UnityEvent onFailure;

    // Called by Ardity for every line received from the Arduino
    void OnMessageArrived(string msg)
    {
        msg = msg.Trim();

        if (msg == "SUCCESS")
        {
            Debug.Log("Code correct!");
            onSuccess.Invoke();
        }
        else if (msg == "FAILURE")
        {
            Debug.Log("Code incorrect.");
            onFailure.Invoke();
        }
        // other lines (e.g. "CODE_SET", "ERROR") are ignored here
    }

    // Called by Ardity when the port connects/disconnects
    void OnConnectionEvent(bool success)
    {
        Debug.Log(success ? "Arduino connected" : "Arduino disconnected");
    }

    // Optional: change the code from Unity, e.g. SetCode("101100111")
    public void SetCode(string nineBits)
    {
        var controller = GetComponent<SerialController>();
        if (controller != null) controller.SendSerialMessage("SET " + nineBits);
    }
}