using UnityEngine;

/// <summary>
/// Arduino Uno two-pad input for DinoPadGame, using the Ardity asset for serial I/O.
/// Ardity's SerialController (on this same GameObject) owns the port and the reader
/// thread; it calls OnMessageArrived / OnConnectionEvent on this component from
/// Unity's main thread, so no threading or queueing is needed here.
/// Requires Api Compatibility Level = .NET Framework (an Ardity requirement).
/// Uno sketch emits S,J,D frames, e.g. "S,1,0", with heartbeat frames.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SerialController))]
public sealed class DinoPadUnoSerial : MonoBehaviour
{
    [Header("Serial settings live on the Ardity SerialController component")]
    [Tooltip("Set Port Name (COM3, /dev/cu.usbmodem..., etc.) and Baud Rate = 115200 on SerialController.")]
    public SerialController serialController;

    [Header("Game target")]
    public DinoPadGame game;

    public string ConnectionStatus { get { return status; } }
    public bool IsConnected { get { return connected; } }
    public bool JumpPadPressed { get { return jumpPressed; } }
    public bool DuckPadPressed { get { return duckPressed; } }

    private string status = "Not connected";
    private bool connected;
    private bool portOpen;
    private bool jumpPressed;
    private bool duckPressed;
    private float lastFrameTime;

    private void Awake()
    {
        if (game == null) game = GetComponent<DinoPadGame>();
        if (serialController == null) serialController = GetComponent<SerialController>();
        // Ardity delivers messages to this object.
        if (serialController != null && serialController.messageListener == null)
            serialController.messageListener = gameObject;
    }

    private void OnEnable()
    {
        status = "Waiting for Uno...";
    }

    // ---- Ardity callbacks (invoked via SendMessage by SerialController) ----

    private void OnConnectionEvent(bool success)
    {
        portOpen = success;
        if (!success)
        {
            lastFrameTime = 0f;
            ApplyPads(false, false); // avoid a stuck duck key
            status = "Disconnected - retrying...";
        }
        else status = "Port open - waiting for Uno frames...";
    }

    private void OnMessageArrived(string msg)
    {
        bool j, d;
        if (!ParseState(msg, out j, out d)) return; // ignore startup noise
        lastFrameTime = Time.realtimeSinceStartup;
        portOpen = true;
        ApplyPads(j, d);
    }

    // -----------------------------------------------------------------------

    private void Update()
    {
        // No frames (e.g. Uno unplugged) -> release switches, report not connected.
        bool fresh = lastFrameTime > 0 &&
                     Time.realtimeSinceStartup - lastFrameTime < 0.65f;
        if (!fresh && (jumpPressed || duckPressed)) ApplyPads(false, false);

        connected = portOpen && fresh;
        if (connected)
            status = "Connected - jump=" + (jumpPressed ? "1" : "0") +
                     " duck=" + (duckPressed ? "1" : "0");
        else if (portOpen)
            status = "Port open - no frames from Uno";

        if (game != null) game.InputStatus = status;
    }

    private void ApplyPads(bool jump, bool duck)
    {
        // Only trigger on a press EDGE, not on every heartbeat frame.
        bool jumpEdge = jump && !jumpPressed;
        jumpPressed = jump;
        if (duck != duckPressed)
        {
            duckPressed = duck;
            if (game != null) game.SetDuckPressed(duck, "Arduino D3");
        }
        if (jumpEdge && game != null) game.TriggerJump("Arduino D2");
    }

    private static bool ParseState(string frame, out bool jump, out bool duck)
    {
        jump = false;
        duck = false;
        if (frame == null) return false;
        frame = frame.Trim();
        // Strictly match the sketch's S,0,1 protocol.
        if (frame.Length != 5 || frame[0] != 'S' || frame[1] != ',' ||
            frame[3] != ',' || (frame[2] != '0' && frame[2] != '1') ||
            (frame[4] != '0' && frame[4] != '1')) return false;
        jump = frame[2] == '1';
        duck = frame[4] == '1';
        return true;
    }

    private void OnDisable()
    {
        ApplyPads(false, false);
        connected = false;
        portOpen = false;
        if (game != null) game.InputStatus = "Serial stopped";
    }
}