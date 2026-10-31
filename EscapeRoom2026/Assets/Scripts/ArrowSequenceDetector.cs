using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum Arrow { Up, Down, Left, Right }

/// <summary>
/// Detects when a sequence of arrow presses has been entered in order.
/// The sequence is edited in the Inspector. Listens to the keyboard arrow keys
/// and, optionally, to the Arduino pads via DinoPadUnoSerial.
/// Other scripts can also feed it with PressArrow(Arrow.Left), etc.
/// </summary>
public sealed class ArrowSequenceDetector : MonoBehaviour
{
    [Header("Sequence (edit in the Inspector)")]
    public List<Arrow> sequence = new List<Arrow>
    {
        Arrow.Up, Arrow.Up, Arrow.Down, Arrow.Down,
        Arrow.Left, Arrow.Right, Arrow.Left, Arrow.Right
    };

    [Header("Rules")]
    [Tooltip("Seconds allowed between presses before progress resets. 0 = no time limit.")]
    [Min(0f)] public float timeBetweenPresses = 0f;
    [Tooltip("Reset progress to zero after a successful sequence so it can be entered again.")]
    public bool resetAfterSuccess = true;

    [Header("Input sources")]
    public bool useKeyboard = true;
    [Tooltip("Optional: also read the Arduino pads (S,U,D,L,R). Leave empty to ignore.")]
    public DinoPadUnoSerial arduino;

    [Header("Events")]
    [Tooltip("Fired when the full sequence has been entered correctly.")]
    public UnityEvent onSequenceComplete = new UnityEvent();
    [Tooltip("Fired for each correct press that doesn't finish the sequence (arg = steps matched so far).")]
    public UnityEvent<int> onProgress = new UnityEvent<int>();
    [Tooltip("Fired when a wrong arrow breaks the sequence or the timer runs out.")]
    public UnityEvent onSequenceFailed = new UnityEvent();

    [Header("Debug")]
    public bool logToConsole = true;

    /// <summary>Number of arrows matched so far.</summary>
    public int Progress { get { return index; } }
    public bool Completed { get; private set; }

    private int index;
    private float lastPressTime;
    private bool prevUp, prevDown, prevLeft, prevRight;

    public GameObject hiddenText;   // drag the text object here in the Inspector

    public void ShowText()
    {
        hiddenText.SetActive(true);
    }

    private void Update()
    {
        // Timeout between presses
        if (index > 0 && timeBetweenPresses > 0f &&
            Time.time - lastPressTime > timeBetweenPresses)
        {
            if (logToConsole) Debug.Log("Arrow sequence timed out");
            Fail();
        }

        if (useKeyboard) ReadKeyboard();
        if (arduino != null) ReadArduino();
    }

    private void ReadKeyboard()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.UpArrow)) PressArrow(Arrow.Up);
        if (Input.GetKeyDown(KeyCode.DownArrow)) PressArrow(Arrow.Down);
        if (Input.GetKeyDown(KeyCode.LeftArrow)) PressArrow(Arrow.Left);
        if (Input.GetKeyDown(KeyCode.RightArrow)) PressArrow(Arrow.Right);
#endif
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.upArrowKey.wasPressedThisFrame) PressArrow(Arrow.Up);
            if (kb.downArrowKey.wasPressedThisFrame) PressArrow(Arrow.Down);
            if (kb.leftArrowKey.wasPressedThisFrame) PressArrow(Arrow.Left);
            if (kb.rightArrowKey.wasPressedThisFrame) PressArrow(Arrow.Right);
        }
#endif
    }

    // Detect released -> pressed edges on the Arduino pads.
    private void ReadArduino()
    {
        bool up = arduino.UpPressed, down = arduino.DownPressed;
        bool left = arduino.LeftPressed, right = arduino.RightPressed;
        if (up && !prevUp) PressArrow(Arrow.Up);
        if (down && !prevDown) PressArrow(Arrow.Down);
        if (left && !prevLeft) PressArrow(Arrow.Left);
        if (right && !prevRight) PressArrow(Arrow.Right);
        prevUp = up; prevDown = down; prevLeft = left; prevRight = right;
    }

    /// <summary>Feed one arrow press into the detector (from any source).</summary>
    public void PressArrow(Arrow arrow)
    {
        if (sequence == null || sequence.Count == 0) return;

        // After completing without auto-reset, ignore input until ResetProgress().
        if (Completed && !resetAfterSuccess) return;

        lastPressTime = Time.time;

        if (arrow == sequence[index])
        {
            index++;
            if (logToConsole)
                Debug.Log("Arrow " + arrow + " correct (" + index + "/" + sequence.Count + ")");

            if (index >= sequence.Count)
            {
                if (logToConsole) Debug.Log("Arrow sequence COMPLETE");
                Completed = true;
                onSequenceComplete.Invoke();
                if (resetAfterSuccess) ResetProgress();
            }
            else onProgress.Invoke(index);
        }
        else
        {
            if (logToConsole)
                Debug.Log("Arrow " + arrow + " wrong (expected " + sequence[index] + ")");
            Fail();
            // A wrong press that equals the first arrow starts a new attempt.
            if (arrow == sequence[0])
            {
                index = 1;
                lastPressTime = Time.time;
                onProgress.Invoke(index);
            }
        }
    }

    /// <summary>Clear progress and the completed flag.</summary>
    public void ResetProgress()
    {
        index = 0;
        Completed = false;
    }

    /// <summary>Replace the sequence at runtime, e.g. SetSequence("UDLR").</summary>
    public void SetSequence(string letters)
    {
        List<Arrow> next = new List<Arrow>();
        foreach (char c in letters.ToUpperInvariant())
        {
            if (c == 'U') next.Add(Arrow.Up);
            else if (c == 'D') next.Add(Arrow.Down);
            else if (c == 'L') next.Add(Arrow.Left);
            else if (c == 'R') next.Add(Arrow.Right);
        }
        sequence = next;
        ResetProgress();
    }

    private void Fail()
    {
        bool hadProgress = index > 0;
        index = 0;
        if (hadProgress) onSequenceFailed.Invoke();
    }


}