using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Self-contained 2-input Dino runner. No prefabs, sprites, fonts or UI packages needed.
/// Drop in a Unity scene and press Play. Uses screen-space IMGUI for portable 2D visuals.
/// Supports keyboard, mouse, multi-touch, and optional external Arduino input.
/// </summary>
public sealed class DinoPadGame : MonoBehaviour
{
    [Header("Scoring")]
    [Min(50)] public int winningScore = 500;
    [Min(1)] public int goalStep = 50;

    [Header("Button Press History")]
    [Min(1)] [Tooltip("Keep only the latest N new button presses. Change this in the Inspector.")]
    public int lastNPresses = 10;
    [Tooltip("Show the history below the game. It can be scrolled when there are more entries.")]
    public bool showButtonHistory = true;
    [Tooltip("Also print each new press in the Unity Console for debugging.")]
    public bool printPressesToConsole = false;

    [Serializable]
    public class ButtonPressRecord
    {
        public string button;
        public string source;
        public int scoreAtPress;
        public float elapsedSeconds;
    }

    private readonly List<ButtonPressRecord> buttonHistory = new List<ButtonPressRecord>();
    // Newest entry first. Read-only prevents external code accidentally changing it.
    public IList<ButtonPressRecord> RecentButtonPresses { get { return buttonHistory.AsReadOnly(); } }

    [Header("Difficulty")]
    [Min(100)] public float startingSpeed = 380f;
    [Min(100)] public float maximumSpeed = 680f;

    [Header("Signals / Arduino integration")]
    [Tooltip("Called once when the score goal is reached (per run).")]
    public UnityEvent onGoalReached = new UnityEvent();
    [Tooltip("Called when the dinosaur collides with an obstacle.")]
    public UnityEvent onGameOver = new UnityEvent();

    public int Score { get { return Mathf.FloorToInt(points); } }
    public int BestScore { get { return bestScore; } }
    public bool IsRunning { get { return state == GameState.Running; } }
    public bool HasWon { get { return state == GameState.Won; } }
    [NonSerialized] public string InputStatus = "Keyboard / touch only";

    private const float DesignWidth = 1000f;
    private const float DesignHeight = 800f;
    private const float FieldWidth = 950f;
    private const float FieldHeight = 285f;
    private const float GroundY = 225f;
    private const float DinosaurX = 110f;
    private const string BestKey = "DinoPad_Unity_Best";
    private const string GoalKey = "DinoPad_Unity_Goal";

    private enum GameState { Ready, Running, Crashed, Won }
    private enum ObstacleKind { SmallCactus, TallCactus, DoubleCactus, Bird }

    private class Obstacle
    {
        public float x, y, width, height;
        public ObstacleKind kind;
    }

    private class Cloud
    {
        public float x, y, speed;
    }

    private GameState state = GameState.Ready;
    private readonly List<Obstacle> obstacles = new List<Obstacle>();
    private readonly List<Cloud> clouds = new List<Cloud>();
    private float points;
    private int bestScore;
    private float speed;
    private float spawnTimer;
    private float bottom;
    private float verticalSpeed;
    private float distance;
    private float groundOffset;
    private float elapsed;
    private bool ducking;
    private bool jumpWasHeld;
    private bool localDuckWasHeld;
    private Vector2 historyScroll;
    private bool externalDuckHeld;
    private float uiScale;
    private float uiOffsetX;
    private float uiOffsetY;
    private GUIStyle labelStyle, smallStyle, tinyStyle, numberStyle, centeredStyle,
                     centeredSmallStyle, bigCenteredStyle, buttonStyle;

    private static readonly Rect JumpPad = new Rect(44, 476, 214, 96);
    private static readonly Rect DuckPad = new Rect(271, 476, 214, 96);
    private static readonly Rect Field = new Rect(25, 110, FieldWidth, FieldHeight);

    private static readonly Color BackgroundColor = Hex("#EDF4F5");
    private static readonly Color Ink = Hex("#263940");
    private static readonly Color InkSoft = Hex("#526E76");
    private static readonly Color Pale = Hex("#D8E7E9");
    private static readonly Color Teal = Hex("#087E76");
    private static readonly Color TealPale = Hex("#E4F4F1");
    private static readonly Color White = Color.white;

    private static Color Hex(string text)
    {
        Color parsed;
        return ColorUtility.TryParseHtmlString(text, out parsed) ? parsed : Color.white;
    }

    private void Awake()
    {
        Application.targetFrameRate = 60;
        bestScore = Mathf.Max(0, PlayerPrefs.GetInt(BestKey, 0));
        // The saved goal is optional; new installs use the Inspector value.
        winningScore = Mathf.Clamp(PlayerPrefs.GetInt(GoalKey, winningScore), 50, 100000);
        speed = startingSpeed;
        bottom = GroundY;
        clouds.Add(new Cloud { x = 115, y = 60, speed = 13 });
        clouds.Add(new Cloud { x = 480, y = 82, speed = 17 });
        clouds.Add(new Cloud { x = 790, y = 40, speed = 11 });
        RefreshLayout();
    }

    private void OnValidate()
    {
        winningScore = Mathf.Clamp(winningScore, 50, 100000);
        lastNPresses = Mathf.Clamp(lastNPresses, 1, 100);
        goalStep = Mathf.Max(1, goalStep);
        startingSpeed = Mathf.Max(100, startingSpeed);
        maximumSpeed = Mathf.Max(startingSpeed, maximumSpeed);
    }

    private void RefreshLayout()
    {
        uiScale = Mathf.Min(Screen.width / DesignWidth, Screen.height / DesignHeight);
        uiScale = Mathf.Max(0.01f, uiScale);
        uiOffsetX = (Screen.width - DesignWidth * uiScale) * 0.5f;
        uiOffsetY = (Screen.height - DesignHeight * uiScale) * 0.5f;
    }

    private Vector2 ToLayoutPosition(Vector2 screenPosition)
    {
        return new Vector2((screenPosition.x - uiOffsetX) / uiScale,
                           (Screen.height - screenPosition.y - uiOffsetY) / uiScale);
    }

    private void Update()
    {
        RefreshLayout();
        bool jumpHeld = false;
        bool localDuckHeld = false;

        // Compile for legacy-only, Input System-only, and Both project configurations.
#if ENABLE_LEGACY_INPUT_MANAGER
        jumpHeld |= Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);
        localDuckHeld |= Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase == UnityEngine.TouchPhase.Ended || touch.phase == UnityEngine.TouchPhase.Canceled) continue;
            ReadPointer(ToLayoutPosition(touch.position), ref jumpHeld, ref localDuckHeld);
        }
        // Unity maps touch to mouse in some configurations; this is safe with OR/edge detection.
        if (Input.GetMouseButton(0))
            ReadPointer(ToLayoutPosition(Input.mousePosition), ref jumpHeld, ref localDuckHeld);
#endif
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            jumpHeld |= keyboard.spaceKey.isPressed || keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed;
            localDuckHeld |= keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed;
        }
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            foreach (var touch in touchscreen.touches)
            {
                if (!touch.press.isPressed) continue;
                ReadPointer(ToLayoutPosition(touch.position.ReadValue()), ref jumpHeld, ref localDuckHeld);
            }
        }
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
            ReadPointer(ToLayoutPosition(mouse.position.ReadValue()), ref jumpHeld, ref localDuckHeld);
#endif
        ducking = localDuckHeld || externalDuckHeld;
        // Track local inputs independently of the Arduino so overlapping presses
        // are counted correctly and holding an input creates only one entry.
        if (localDuckHeld && !localDuckWasHeld)
            RememberPress("DUCK", "Keyboard / Touch");
        localDuckWasHeld = localDuckHeld;
        if (jumpHeld && !jumpWasHeld)
            TriggerJump("Keyboard / Touch");
        jumpWasHeld = jumpHeld;
        TrimButtonHistory();

        float dt = Mathf.Min(Time.deltaTime, 0.036f);
        elapsed += dt;
        foreach (Cloud cloud in clouds)
        {
            cloud.x -= cloud.speed * dt;
            if (cloud.x < -75) cloud.x = FieldWidth + 75;
        }
        if (state != GameState.Running) return;

        distance += speed * dt;
        groundOffset = (groundOffset + speed * dt) % 70;
        points += speed * dt / 10f;
        speed = Mathf.Min(maximumSpeed, startingSpeed + Mathf.Floor(points / 80f) * 14f);

        if (bottom < GroundY || verticalSpeed < 0)
        {
            verticalSpeed += 2300f * dt;
            bottom += verticalSpeed * dt;
            if (bottom >= GroundY) { bottom = GroundY; verticalSpeed = 0; }
        }

        spawnTimer -= dt;
        if (spawnTimer <= 0) SpawnObstacle();
        for (int i = obstacles.Count - 1; i >= 0; i--)
        {
            Obstacle obstacle = obstacles[i];
            obstacle.x -= speed * dt;
            if (obstacle.x + obstacle.width < -50) obstacles.RemoveAt(i);
        }

        bool low = ducking && OnGround;
        Rect dinosaur = low
            ? new Rect(DinosaurX + 6, GroundY - 34, 58, 30)
            : new Rect(DinosaurX + 18, bottom - 61, 49, 57);
        foreach (Obstacle obstacle in obstacles)
        {
            Rect collider = obstacle.kind == ObstacleKind.Bird
                ? new Rect(obstacle.x + 11, obstacle.y + 6, 40, 15)
                : new Rect(obstacle.x + 5, GroundY - obstacle.height + 3,
                           obstacle.width - 8, obstacle.height - 4);
            if (dinosaur.Overlaps(collider))
            {
                EndGame(false);
                return;
            }
        }
        if (points >= winningScore) EndGame(true);
        if (Score > bestScore) bestScore = Score;
    }

    private void ReadPointer(Vector2 position, ref bool jump, ref bool duck)
    {
        if (JumpPad.Contains(position) || (state == GameState.Running && Field.Contains(position))) jump = true;
        if (DuckPad.Contains(position)) duck = true;
    }

    private bool OnGround { get { return bottom >= GroundY - 0.1f; } }

    /// <summary>One jump pulse; keeps the original parameterless Unity API.</summary>
    public void TriggerJump() { TriggerJump("External"); }

    /// <summary>Tagged jump input from keyboard, touch, or Arduino.</summary>
    public void TriggerJump(string source)
    {
        RememberPress("JUMP", source);
        if (state != GameState.Running) StartGame();
        if (state == GameState.Running && OnGround && !ducking) verticalSpeed = -790f;
    }

    /// <summary>Hold true while ducking; preserves the original Unity API.</summary>
    public void SetDuckPressed(bool pressed) { SetDuckPressed(pressed, "External"); }

    /// <summary>Tagged duck input from keyboard, touch, or Arduino.</summary>
    public void SetDuckPressed(bool pressed, string source)
    {
        if (pressed && !externalDuckHeld)
            RememberPress("DUCK", source);
        externalDuckHeld = pressed;
        if (pressed && state == GameState.Ready) StartGame();
    }

    /// <summary>Record one new press; newest first, retaining only last N.</summary>
    private void RememberPress(string button, string source)
    {
        ButtonPressRecord record = new ButtonPressRecord
        {
            button = button,
            source = source,
            scoreAtPress = Score,
            elapsedSeconds = Time.timeSinceLevelLoad
        };
        buttonHistory.Insert(0, record);
        TrimButtonHistory();
        if (printPressesToConsole)
            Debug.Log("DinoPad input: " + button + " from " + source +
                      " at score " + record.scoreAtPress);
    }

    private void TrimButtonHistory()
    {
        int capacity = Mathf.Clamp(lastNPresses, 1, 100);
        if (buttonHistory.Count > capacity)
            buttonHistory.RemoveRange(capacity, buttonHistory.Count - capacity);
    }

    public void ClearButtonHistory()
    {
        buttonHistory.Clear();
        historyScroll = Vector2.zero;
    }

    public void StartGame()
    {
        obstacles.Clear();
        points = 0;
        distance = 0;
        groundOffset = 0;
        bottom = GroundY;
        verticalSpeed = 0;
        speed = startingSpeed;
        spawnTimer = 1.1f;
        state = GameState.Running;
    }

    public void Restart() { StartGame(); }

    public void SetWinningScore(int score)
    {
        winningScore = Mathf.Clamp(score, 50, 100000);
        PlayerPrefs.SetInt(GoalKey, winningScore);
        PlayerPrefs.Save();
        if (state == GameState.Running && points >= winningScore) EndGame(true);
    }

    private void EndGame(bool won)
    {
        if (state != GameState.Running) return;
        state = won ? GameState.Won : GameState.Crashed;
        if (Score > bestScore) bestScore = Score;
        PlayerPrefs.SetInt(BestKey, bestScore);
        PlayerPrefs.Save();
        if (won) onGoalReached.Invoke();
        else onGameOver.Invoke();
    }

    private void SpawnObstacle()
    {
        float roll = UnityEngine.Random.value;
        Obstacle obstacle = new Obstacle { x = FieldWidth + 30 };
        if (points > 65 && roll > 0.67f)
        {
            obstacle.kind = ObstacleKind.Bird;
            obstacle.y = GroundY - 70;
            obstacle.width = 57;
            obstacle.height = 22;
        }
        else if (roll > 0.44f)
        {
            obstacle.kind = ObstacleKind.TallCactus;
            obstacle.width = 31;
            obstacle.height = UnityEngine.Random.Range(49f, 66f);
        }
        else if (roll > 0.17f)
        {
            obstacle.kind = ObstacleKind.DoubleCactus;
            obstacle.width = 53;
            obstacle.height = 52;
        }
        else
        {
            obstacle.kind = ObstacleKind.SmallCactus;
            obstacle.width = 24;
            obstacle.height = 42;
        }
        obstacles.Add(obstacle);
        spawnTimer = UnityEngine.Random.Range(1.15f, 1.75f) * startingSpeed / speed + 0.18f;
    }

    private void OnGUI()
    {
        if (labelStyle == null) MakeStyles();
        RefreshLayout();
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        DrawSolid(new Rect(0, 0, Screen.width, Screen.height), BackgroundColor);
        GUI.matrix = Matrix4x4.TRS(new Vector3(uiOffsetX, uiOffsetY, 0),
                                   Quaternion.identity, new Vector3(uiScale, uiScale, 1));
        DrawInterface();
        if (showButtonHistory) DrawButtonHistory();
        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
    }

    private void MakeStyles()
    {
        labelStyle = TextStyle(15, FontStyle.Bold, Ink, TextAnchor.MiddleLeft);
        smallStyle = TextStyle(13, FontStyle.Normal, InkSoft, TextAnchor.MiddleLeft);
        tinyStyle = TextStyle(11, FontStyle.Bold, InkSoft, TextAnchor.MiddleLeft);
        numberStyle = TextStyle(28, FontStyle.Bold, Ink, TextAnchor.MiddleLeft);
        centeredStyle = TextStyle(20, FontStyle.Bold, Ink, TextAnchor.MiddleCenter);
        centeredSmallStyle = TextStyle(14, FontStyle.Normal, InkSoft, TextAnchor.MiddleCenter);
        bigCenteredStyle = TextStyle(33, FontStyle.Bold, Ink, TextAnchor.MiddleCenter);
        buttonStyle = TextStyle(19, FontStyle.Bold, White, TextAnchor.MiddleCenter);
    }

    private static GUIStyle TextStyle(int size, FontStyle weight, Color color, TextAnchor alignment)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = size;
        style.fontStyle = weight;
        style.alignment = alignment;
        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        style.wordWrap = true;
        return style;
    }

    private static void DrawSolid(Rect area, Color color)
    {
        Color saved = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(area, Texture2D.whiteTexture, ScaleMode.StretchToFill);
        GUI.color = saved;
    }

    private static void Border(Rect area, Color color, float thickness = 2)
    {
        DrawSolid(new Rect(area.x, area.y, area.width, thickness), color);
        DrawSolid(new Rect(area.x, area.yMax - thickness, area.width, thickness), color);
        DrawSolid(new Rect(area.x, area.y, thickness, area.height), color);
        DrawSolid(new Rect(area.xMax - thickness, area.y, thickness, area.height), color);
    }

    private static void Label(Rect area, string value, GUIStyle style)
    {
        GUI.Label(area, value, style);
    }

    private void DrawInterface()
    {
        DrawSolid(new Rect(10, 10, 980, 394), White);
        Border(new Rect(10, 10, 980, 394), Pale);
        DrawStat(34, "CURRENT SCORE", Score.ToString("00000"), Ink);
        DrawStat(278, "PERSONAL BEST", bestScore.ToString("00000"), Ink);
        DrawStat(522, "WIN AT", winningScore.ToString("00000"), Teal);
        DrawStat(766, "GAME SPEED", (speed / startingSpeed).ToString("0.0") + "x", Ink);
        DrawSolid(new Rect(18, 99, 964, 2), Pale);

        GUI.BeginGroup(Field);
        DrawField();
        GUI.EndGroup();

        DrawSolid(new Rect(10, 418, 485, 179), White);
        DrawSolid(new Rect(505, 418, 485, 179), White);
        Border(new Rect(10, 418, 485, 179), Pale);
        Border(new Rect(505, 418, 485, 179), Pale);
        Label(new Rect(30, 427, 330, 30), "TWO-PAD CONTROLS", tinyStyle);
        Label(new Rect(525, 427, 340, 30), "GAME SETTINGS", tinyStyle);

        DrawPad(JumpPad, "JUMP", "SPACE / UP", Teal, TealPale);
        DrawPad(DuckPad, "DUCK", "DOWN / HOLD", Ink, Hex("#E9F0F2"));

        Label(new Rect(527, 469, 170, 32), "WINNING SCORE", smallStyle);
        Label(new Rect(698, 470, 101, 34), winningScore.ToString(), numberStyle);
        if (FlatButton(new Rect(810, 467, 67, 42), "-", Hex("#E3F1EF"), Teal, 25))
            SetWinningScore(winningScore - goalStep);
        if (FlatButton(new Rect(887, 467, 67, 42), "+", Teal, White, 25))
            SetWinningScore(winningScore + goalStep);

        string status = state == GameState.Ready ? "Ready to play" :
                        state == GameState.Running ? "Playing - avoid the obstacles" :
                        state == GameState.Won ? "Success! You reached the target." :
                        "Game over - try again";
        Label(new Rect(527, 523, 440, 34), status, labelStyle);
        Label(new Rect(527, 554, 445, 27), "UNO USB: " + InputStatus, smallStyle);

        DrawSolid(new Rect(10, 612, 980, 60), White);
        Border(new Rect(10, 612, 980, 60), Pale, 1);
        Label(new Rect(28, 616, 420, 28), "PROGRESS TO YOUR GOAL", tinyStyle);
        Label(new Rect(720, 616, 245, 28), Score + " / " + winningScore, smallStyle);
        DrawSolid(new Rect(28, 648, 944, 9), Hex("#E2ECEE"));
        DrawSolid(new Rect(28, 648, 944 * Mathf.Clamp01(points / winningScore), 9), Teal);
    }

    private void DrawButtonHistory()
    {
        Rect panel = new Rect(10, 683, 980, 103);
        DrawSolid(panel, White);
        Border(panel, Pale, 1);
        Label(new Rect(29, 687, 620, 24), "LAST " + lastNPresses +
              " BUTTON PRESSES  (newest first)", tinyStyle);
        if (FlatButton(new Rect(857, 686, 115, 25), "CLEAR", TealPale, Teal, 13))
            ClearButtonHistory();

        if (buttonHistory.Count == 0)
        {
            Label(new Rect(29, 721, 895, 35),
                  "No presses yet. Use Space / Down Arrow or Arduino D2 / D3.", smallStyle);
            return;
        }

        const float lineHeight = 23f;
        float contentHeight = Mathf.Max(59f, buttonHistory.Count * lineHeight + 5f);
        historyScroll = GUI.BeginScrollView(new Rect(29, 718, 943, 58), historyScroll,
                                            new Rect(0, 0, 918, contentHeight));
        for (int i = 0; i < buttonHistory.Count; i++)
        {
            ButtonPressRecord entry = buttonHistory[i];
            float y = i * lineHeight;
            if (i % 2 == 0)
                DrawSolid(new Rect(0, y, 918, lineHeight), Hex("#F3F8F9"));
            Label(new Rect(8, y, 60, lineHeight), "#" + (i + 1), smallStyle);
            Label(new Rect(74, y, 170, lineHeight), entry.button, labelStyle);
            Label(new Rect(258, y, 260, lineHeight), entry.source, smallStyle);
            Label(new Rect(545, y, 177, lineHeight), "Score " + entry.scoreAtPress, smallStyle);
            Label(new Rect(747, y, 155, lineHeight), entry.elapsedSeconds.ToString("0.0") + " s", smallStyle);
        }
        GUI.EndScrollView();
    }

    private void DrawStat(float x, string caption, string value, Color numberColor)
    {
        Label(new Rect(x, 21, 215, 25), caption, tinyStyle);
        Color previous = numberStyle.normal.textColor;
        numberStyle.normal.textColor = numberColor;
        Label(new Rect(x, 47, 215, 46), value, numberStyle);
        numberStyle.normal.textColor = previous;
    }

    private void DrawPad(Rect rect, string name, string hint, Color accent, Color background)
    {
        DrawSolid(rect, background);
        Border(rect, accent, 2);
        GUIStyle padStyle = centeredStyle;
        Color prev = padStyle.normal.textColor;
        padStyle.normal.textColor = accent;
        Label(new Rect(rect.x, rect.y + 16, rect.width, 38), name, padStyle);
        padStyle.normal.textColor = prev;
        Label(new Rect(rect.x, rect.y + 54, rect.width, 28), hint, centeredSmallStyle);
    }

    private bool FlatButton(Rect rect, string text, Color background, Color foreground, int fontSize)
    {
        DrawSolid(rect, background);
        GUIStyle style = new GUIStyle(centeredStyle);
        style.fontSize = fontSize;
        style.normal.textColor = foreground;
        style.hover.textColor = foreground;
        style.active.textColor = foreground;
        Label(rect, text, style);
        return GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    private void DrawField()
    {
        DrawSolid(new Rect(0, 0, FieldWidth, FieldHeight), Hex("#FAFDFE"));
        Label(new Rect(13, 6, 270, 28), "DINO // TWO INPUTS", tinyStyle);
        foreach (Cloud cloud in clouds) DrawCloud(cloud);
        DrawSolid(new Rect(0, GroundY - 1, FieldWidth, 3), Ink);
        DrawSolid(new Rect(0, GroundY + 10, FieldWidth, 1), Hex("#B6CBD0"));
        for (int i = 0; i < 20; i++)
        {
            float x = i * 70 - groundOffset;
            DrawSolid(new Rect(x, GroundY + 13 + i % 3 * 8, 12 + i * 7 % 21, 2), Pale);
        }
        foreach (Obstacle obstacle in obstacles)
        {
            if (obstacle.kind == ObstacleKind.Bird) DrawBird(obstacle);
            else DrawCactus(obstacle);
        }
        DrawDinosaur();
        if (state != GameState.Running) DrawOverlay();
    }

    private void Pixel(float x, float y, float width, float height, Color ink)
    {
        DrawSolid(new Rect(Mathf.Round(x), Mathf.Round(y),
                           Mathf.Round(width), Mathf.Round(height)), ink);
    }

    private void DrawCloud(Cloud c)
    {
        Color color = Hex("#D2E3E6");
        Pixel(c.x - 30, c.y + 6, 63, 2, color);
        Pixel(c.x - 25, c.y - 1, 15, 2, color);
        Pixel(c.x - 12, c.y - 10, 20, 2, color);
        Pixel(c.x + 10, c.y - 2, 20, 2, color);
        Pixel(c.x - 13, c.y - 9, 2, 9, color);
        Pixel(c.x + 7, c.y - 9, 2, 8, color);
        Pixel(c.x - 28, c.y, 2, 7, color);
        Pixel(c.x + 30, c.y - 1, 2, 8, color);
    }

    private void DrawDinosaur()
    {
        float x = DinosaurX;
        bool crouching = ducking && OnGround && state == GameState.Running;
        float y = bottom - (crouching ? 35 : 64);
        if (crouching)
        {
            Pixel(x + 4, y + 13, 48, 20, Ink);
            Pixel(x + 39, y + 1, 26, 22, Ink);
            Pixel(x + 53, y + 17, 18, 6, Ink);
            Pixel(x - 11, y + 20, 20, 7, Ink);
            Pixel(x + 14, y + 30, 8, 8, Ink);
            Pixel(x + 38, y + 30, 8, 8, Ink);
            Pixel(x + 55, y + 6, 4, 4, White);
            return;
        }
        Pixel(x + 18, y + 28, 36, 28, Ink);
        Pixel(x + 38, y + 8, 27, 35, Ink);
        Pixel(x + 45, y + 1, 28, 22, Ink);
        Pixel(x + 57, y + 20, 24, 7, Ink);
        Pixel(x + 16, y + 35, 12, 10, Ink);
        Pixel(x + 7, y + 38, 15, 8, Ink);
        Pixel(x - 5, y + 44, 16, 7, Ink);
        Pixel(x + 29, y + 43, 9, 4, White);
        if (!OnGround)
        {
            Pixel(x + 25, y + 52, 10, 12, Ink);
            Pixel(x + 48, y + 52, 10, 11, Ink);
        }
        else if (Mathf.Sin(distance * 0.056f) > 0)
        {
            Pixel(x + 23, y + 53, 10, 12, Ink);
            Pixel(x + 23, y + 62, 17, 5, Ink);
            Pixel(x + 46, y + 53, 11, 6, Ink);
        }
        else
        {
            Pixel(x + 23, y + 53, 10, 7, Ink);
            Pixel(x + 45, y + 53, 11, 12, Ink);
            Pixel(x + 43, y + 62, 17, 5, Ink);
        }
        Pixel(x + 62, y + 8, 4, 4, White);
    }

    private void DrawCactus(Obstacle obstacle)
    {
        float y = GroundY - obstacle.height;
        if (obstacle.kind == ObstacleKind.DoubleCactus)
        {
            SingleCactus(obstacle.x, y + 8, 28, obstacle.height - 8);
            SingleCactus(obstacle.x + 27, y, 28, obstacle.height);
        }
        else SingleCactus(obstacle.x, y, obstacle.width, obstacle.height);
    }

    private void SingleCactus(float x, float y, float width, float height)
    {
        Pixel(x + width * .35f, y, width * .34f, height, Ink);
        Pixel(x, y + height * .38f, width * .37f, 8, Ink);
        Pixel(x + 1, y + height * .18f, 8, height * .28f, Ink);
        Pixel(x + width * .68f, y + height * .56f, width * .34f, 8, Ink);
        Pixel(x + width * .83f, y + height * .3f, 8, height * .31f, Ink);
    }

    private void DrawBird(Obstacle obstacle)
    {
        float x = obstacle.x, y = obstacle.y;
        Pixel(x + 15, y + 10, 31, 11, Ink);
        Pixel(x + 40, y + 9, 14, 9, Ink);
        Pixel(x + 52, y + 13, 9, 4, Ink);
        Pixel(x + 4, y + 15, 17, 5, Ink);
        Pixel(x + 45, y + 10, 3, 3, White);
        if (Mathf.Sin(elapsed * 14 + x * .02f) > 0)
        {
            Pixel(x + 20, y - 5, 19, 16, Ink);
            Pixel(x + 22, y - 11, 12, 9, Ink);
        }
        else
        {
            Pixel(x + 15, y + 19, 22, 10, Ink);
            Pixel(x + 16, y + 27, 14, 7, Ink);
        }
    }

    private void DrawOverlay()
    {
        DrawSolid(new Rect(0, 0, FieldWidth, FieldHeight), new Color(.97f, 1f, 1f, .45f));
        Rect panel = new Rect(242, 62, 466, 161);
        DrawSolid(panel, White);
        Border(panel, Pale);
        string title = state == GameState.Ready ? "READY TO PLAY?" :
                       state == GameState.Won ? "YOU WIN!" : "GAME OVER";
        string detail = state == GameState.Ready ? "Press JUMP to begin" :
                        state == GameState.Won ? "Goal reached! Score: " + Score :
                        "Score: " + Score + "   /   Goal: " + winningScore;
        Label(new Rect(270, 80, 410, 46), title, bigCenteredStyle);
        Label(new Rect(265, 127, 420, 27), detail, centeredSmallStyle);
        string action = state == GameState.Ready ? "START" : "PLAY AGAIN";
        if (FlatButton(new Rect(390, 166, 170, 40), action, Teal, White, 16))
            StartGame();
    }

    public void SwitchScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}
