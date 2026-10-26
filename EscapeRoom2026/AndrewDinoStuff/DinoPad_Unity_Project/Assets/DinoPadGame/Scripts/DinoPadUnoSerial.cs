using System;
using System.Threading;
using UnityEngine;
#if (UNITY_EDITOR || UNITY_STANDALONE) && !UNITY_WEBGL
using System.IO.Ports;
#endif

/// <summary>
/// Arduino Uno R3/compatible USB serial two-pad input for DinoPadGame.
/// Requires desktop Unity's .NET Framework API Compatibility Level,
/// because SerialPort is not included with Unity's default .NET Standard 2.1.
/// Uno sketch emits S,J,D frames, e.g. "S,1,0\n", with heartbeat frames.
/// The worker only communicates state; all Unity APIs are used on main thread.
/// </summary>
[DisallowMultipleComponent]
public sealed class DinoPadUnoSerial : MonoBehaviour
{
    [Header("UNO USB Serial - Windows / macOS / Linux Standalone")]
    [Tooltip("AUTO uses the only serial port if exactly one is found. Otherwise specify COM3, /dev/cu.usbmodem..., etc. Find it in Arduino IDE > Tools > Port.")]
    public string portName = "AUTO";
    [Tooltip("Matches Serial.begin(115200) in the supplied Uno sketch.")]
    public int baudRate = 115200;
    public bool connectOnStart = true;

    [Header("Game target")]
    public DinoPadGame game;

    public string ConnectionStatus { get { return status; } }
    public bool IsConnected { get { return connected; } }
    public bool JumpPadPressed { get { return jumpPressed; } }
    public bool DuckPadPressed { get { return duckPressed; } }

    private string status = "Not connected";
    private bool connected;
    private bool jumpPressed;
    private bool duckPressed;
    private float lastFrameTime;

#if (UNITY_EDITOR || UNITY_STANDALONE) && !UNITY_WEBGL
    private volatile bool keepRunning;
    private volatile bool workerConnected;
    private volatile string workerStatus = "Searching for Uno...";
    private Thread readerThread;
    private readonly object sync = new object();
    private struct PadFrame
    {
        public bool jump;
        public bool duck;
    }
    private readonly System.Collections.Generic.Queue<PadFrame> pendingFrames =
        new System.Collections.Generic.Queue<PadFrame>();
    private readonly System.Collections.Generic.List<PadFrame> updateFrames =
        new System.Collections.Generic.List<PadFrame>();
    private SerialPort serial;
#endif

    private void Awake()
    {
        if (game == null) game = GetComponent<DinoPadGame>();
    }

    private void OnEnable()
    {
#if (UNITY_EDITOR || UNITY_STANDALONE) && !UNITY_WEBGL
        if (connectOnStart)
        {
            keepRunning = true;
            readerThread = new Thread(SerialLoop);
            readerThread.Name = "DinoPad Uno Serial Reader";
            readerThread.IsBackground = true;
            readerThread.Start();
        }
        else status = "Serial disabled (keyboard works)";
#else
        status = "Serial only in desktop builds";
#endif
    }

    private void Update()
    {
#if (UNITY_EDITOR || UNITY_STANDALONE) && !UNITY_WEBGL
        if (connectOnStart)
        {
            updateFrames.Clear();
            lock (sync)
            {
                // Drain all frames, not just the last one, so a quick press
                // and release between Unity frames still generates an input.
                while (pendingFrames.Count > 0)
                    updateFrames.Add(pendingFrames.Dequeue());
            }
            bool received = updateFrames.Count > 0;
            if (received)
            {
                lastFrameTime = Time.realtimeSinceStartup;
                foreach (PadFrame frame in updateFrames)
                    ApplyPads(frame.jump, frame.duck);
            }

            // Releasing a disconnected switch avoids a stuck duck key.
            bool fresh = received || (lastFrameTime > 0 &&
                       Time.realtimeSinceStartup - lastFrameTime < 0.65f);
            if (!workerConnected || !fresh)
            {
                if (jumpPressed || duckPressed) ApplyPads(false, false);
            }
            connected = workerConnected && fresh;
            status = connected ? "Connected - jump=" + (jumpPressed ? "1" : "0") +
                     " duck=" + (duckPressed ? "1" : "0") : workerStatus;
        }
#endif
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

#if (UNITY_EDITOR || UNITY_STANDALONE) && !UNITY_WEBGL
    private void SerialLoop()
    {
        while (keepRunning)
        {
            string selected = "";
            try
            {
                selected = FindPort();
                if (selected == null)
                {
                    Pause(1000);
                    continue;
                }
                workerStatus = "Opening " + selected + "...";
                SerialPort connection = new SerialPort(selected, baudRate);
                connection.NewLine = "\n";
                connection.ReadTimeout = 150;
                connection.WriteTimeout = 150;
                connection.DtrEnable = true; // Uno may reset when this port is opened.
                serial = connection;
                connection.Open();
                workerStatus = "Waiting for Uno on " + selected + "...";
                DateTime lastPacket = DateTime.UtcNow;
                bool gotFirst = false;

                while (keepRunning && connection.IsOpen)
                {
                    try
                    {
                        string line = connection.ReadLine().Trim();
                        bool j, d;
                        if (ParseState(line, out j, out d))
                        {
                            lock (sync)
                            {
                                // Bound the queue if the Editor pauses for a long time.
                                if (pendingFrames.Count >= 512)
                                    pendingFrames.Dequeue();
                                pendingFrames.Enqueue(new PadFrame { jump = j, duck = d });
                            }
                            gotFirst = true;
                            lastPacket = DateTime.UtcNow;
                            workerConnected = true;
                        }
                    }
                    catch (TimeoutException) { }

                    // Allow time for Arduino's USB-serial auto-reset on connect.
                    if ((DateTime.UtcNow - lastPacket).TotalSeconds > (gotFirst ? 1.5 : 5.0))
                        throw new TimeoutException("No valid serial frames from Uno");
                }
            }
            catch (Exception ex)
            {
                if (keepRunning)
                    workerStatus = "Serial issue: " + ex.Message + " (check port/IDE)";
            }
            finally
            {
                workerConnected = false;
                CloseSerial();
            }
            Pause(1400);
        }
    }

    private string FindPort()
    {
        string choice = (portName ?? "").Trim();
        if (choice.Length > 0 && !choice.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
            return choice;
        string[] ports = SerialPort.GetPortNames();
        Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
        if (ports.Length == 1) return ports[0];
        if (ports.Length == 0)
            workerStatus = "No serial ports found - connect Uno USB";
        else
            workerStatus = "Multiple ports (" + String.Join(", ", ports) +
                           "). Set Port Name in Inspector";
        return null;
    }

    private static bool ParseState(string frame, out bool jump, out bool duck)
    {
        jump = false;
        duck = false;
        // Strictly match the sketch's S,0,1 protocol; ignore startup noise.
        if (frame.Length != 5 || frame[0] != 'S' || frame[1] != ',' ||
            frame[3] != ',' || (frame[2] != '0' && frame[2] != '1') ||
            (frame[4] != '0' && frame[4] != '1')) return false;
        jump = frame[2] == '1';
        duck = frame[4] == '1';
        return true;
    }

    private void Pause(int milliseconds)
    {
        for (int ms = 0; ms < milliseconds && keepRunning; ms += 50)
            Thread.Sleep(50);
    }

    private void CloseSerial()
    {
        SerialPort current = serial;
        serial = null;
        if (current == null) return;
        try { if (current.IsOpen) current.Close(); } catch { }
        try { current.Dispose(); } catch { }
    }
#endif

    private void OnDisable()
    {
#if (UNITY_EDITOR || UNITY_STANDALONE) && !UNITY_WEBGL
        keepRunning = false;
        // Closing unblocks ReadLine even on platforms ignoring ReadTimeout.
        CloseSerial();
        if (readerThread != null && readerThread.IsAlive)
            readerThread.Join(350);
        readerThread = null;
        workerConnected = false;
#endif
        ApplyPads(false, false);
        connected = false;
        if (game != null) game.InputStatus = "Serial stopped";
    }
}
