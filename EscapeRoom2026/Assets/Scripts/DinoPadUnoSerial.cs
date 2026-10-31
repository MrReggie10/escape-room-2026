using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Arduino Uno four-pad input for DinoPadGame, using the Ardity asset for serial I/O.
/// Ardity's SerialController (on this same GameObject) owns the port and the reader
/// thread; it calls OnMessageArrived / OnConnectionEvent on this component from
/// Unity's main thread, so no threading or queueing is needed here.
/// Requires Api Compatibility Level = .NET Framework (an Ardity requirement).
///
/// Serial protocol: one line  S,U,D,L,R  with each field 0 or 1, e.g. "S,1,0,0,1".
///   U = up    -> jump           D = down  -> duck (held)
///   L = left  -> onLeftPressed  R = right -> onRightPressed
/// Frames are sent on change plus as a heartbeat.
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

    [Header("Left / Right pad events (no built-in game action)")]
    [Tooltip("Invoked once each time LEFT goes from released to pressed.")]
    public UnityEvent onLeftPressed = new UnityEvent();
    [Tooltip("Invoked once each time RIGHT goes from released to pressed.")]
    public UnityEvent onRightPressed = new UnityEvent();

    public string ConnectionStatus { get { return status; } }
    public bool IsConnected { get { return connected; } }
    public bool UpPressed { get { return upPressed; } }
    public bool DownPressed { get { return downPressed; } }
    public bool LeftPressed { get { return leftPressed; } }
    public bool RightPressed { get { return rightPressed; } }

    private string status = "Not connected";
    private bool connected;
    private bool portOpen;
    private bool upPressed;
    private bool downPressed;
    private bool leftPressed;
    private bool rightPressed;
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
            ApplyPads(false, false, false, false); // avoid a stuck duck key
            status = "Disconnected - retrying...";
        }
        else status = "Port open - waiting for Uno frames...";
    }

    private void OnMessageArrived(string msg)
    {
        bool u, d, l, r;
        if (!ParseState(msg, out u, out d, out l, out r)) return; // ignore startup noise
        lastFrameTime = Time.realtimeSinceStartup;
        portOpen = true;
        ApplyPads(u, d, l, r);
    }

    // -----------------------------------------------------------------------

    private void Update()
    {
        // No frames (e.g. Uno unplugged) -> release pads, report not connected.
        bool fresh = lastFrameTime > 0 &&
                     Time.realtimeSinceStartup - lastFrameTime < 0.65f;
        if (!fresh && (upPressed || downPressed || leftPressed || rightPressed))
            ApplyPads(false, false, false, false);

        connected = portOpen && fresh;
        if (connected)
            status = "Connected - U=" + (upPressed ? "1" : "0") +
                     " D=" + (downPressed ? "1" : "0") +
                     " L=" + (leftPressed ? "1" : "0") +
                     " R=" + (rightPressed ? "1" : "0");
        else if (portOpen)
            status = "Port open - no frames from Uno";

        if (game != null) game.InputStatus = status;
    }

    private void ApplyPads(bool up, bool down, bool left, bool right)
    {
        // Edges only, so repeated heartbeat frames don't retrigger anything.
        bool upEdge = up && !upPressed;
        bool leftEdge = left && !leftPressed;
        bool rightEdge = right && !rightPressed;

        upPressed = up;
        leftPressed = left;
        rightPressed = right;

        if (down != downPressed)
        {
            downPressed = down;
            if (game != null) game.SetDuckPressed(down, "Arduino D3 (down)");
        }
        if (upEdge && game != null) game.TriggerJump("Arduino D2 (up)");
        if (leftEdge)
        {
            if (game != null) game.RegisterExtraPress("LEFT", "Arduino D4");
            onLeftPressed.Invoke();
        }
        if (rightEdge)
        {
            if (game != null) game.RegisterExtraPress("RIGHT", "Arduino D5");
            onRightPressed.Invoke();
        }
    }

    // Strictly matches "S,U,D,L,R" where each of U,D,L,R is '0' or '1' (9 chars).
    private static bool ParseState(string frame, out bool up, out bool down,
                                   out bool left, out bool right)
    {
        up = down = left = right = false;
        if (frame == null) return false;
        frame = frame.Trim();
        if (frame.Length != 9 || frame[0] != 'S' ||
            frame[1] != ',' || frame[3] != ',' || frame[5] != ',' || frame[7] != ',')
            return false;
        for (int i = 2; i <= 8; i += 2)
            if (frame[i] != '0' && frame[i] != '1') return false;
        up = frame[2] == '1';
        down = frame[4] == '1';
        left = frame[6] == '1';
        right = frame[8] == '1';
        return true;
    }

    private void OnDisable()
    {
        ApplyPads(false, false, false, false);
        connected = false;
        portOpen = false;
        if (game != null) game.InputStatus = "Serial stopped";
    }
}