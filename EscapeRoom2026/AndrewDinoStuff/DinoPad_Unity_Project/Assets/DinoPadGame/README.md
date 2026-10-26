# Dino Pad Game - Arduino Uno R3 + Unity desktop

## Software

- Unity Hub and a supported Unity LTS Editor (2D project, Windows/macOS desktop).
- Arduino IDE 2.
- Optional C# editor: Visual Studio or VS Code.

The Uno R3 is a USB serial device, **not** a USB keyboard. This project uses direct serial communication in Unity; no Python intermediary is needed. It is intended for Windows/macOS desktop Unity builds, not WebGL or iPad builds.

## Connection

``
Uno D2 ---- normally-open JUMP switch ---- GND
Uno D3 ---- normally-open DUCK switch ---- GND
Uno USB --------------------------------- Computer
``

The Arduino sketch configures D2 and D3 as `INPUT_PULLUP`. Pads need two metal contacts that connect when stepped on; no 5V lead or external pull-up resistors needed.

## Arduino IDE setup

1. Connect Arduino Uno to your computer with a *data-capable* USB cable.
2. Open `DinoPad_Uno.ino` in Arduino IDE 2.
3. Select **Tools > Board > Arduino AVR Boards > Arduino Uno** and your port under **Tools > Port**.
4. Upload.
5. Optional wiring test: open Serial Monitor at **115200 baud**. Check these expected messages:
   - `S,0,0` neither pad pressed
   - `S,1,0` jump pressed
   - `S,0,1` duck pressed
   - `S,1,1` both pressed
   Data is emitted when pressed states change, plus a heartbeat approximately every 120 ms.
6. **Close Serial Monitor before testing the Unity game.** It locks the port.

## Unity setup

1. Create/open a 2D project using Unity Hub.
2. In **Edit > Project Settings > Player > Other Settings > API Compatibility Level** select **.NET Framework**. `System.IO.Ports.SerialPort` requires this in the supported Unity desktop Mono runtime. Avoid Unity CoreCLR scripting runtime for this package.
3. Import `DinoPad_Unity_Uno_Latest.unitypackage` using **Assets > Import Package > Custom Package**. Or copy the included `Assets/DinoPadGame` folder from ZIP into your project `Assets` folder.
4. Open `Assets/DinoPadGame/Scenes/DinoPadGame.unity`.
5. Click the `Dino Pad Game` GameObject. Its `Dino Pad Uno Serial` component has a **Port Name** field. Set it to the same port seen in Arduino IDE (for example, `COM4` Windows, `/dev/cu.usbmodem1101` macOS). `AUTO` only works if exactly one serial port is detected.
6. Confirm **Baud Rate = 115200** and **Connect On Start = true**, then click **Play**.
7. Allow the Uno a moment to reset after the port opens. The game shows `UNO USB: Connected` when it gets valid packets.

You can always test without the Uno: **Space/Up = Jump, Down = Duck**, and there are also on-screen touch/click pads. Disable `Connect On Start` if you do not want port retries.

## Scoring

`DinoPadGame.cs` controls the game and exposes `winningScore` (default 500), `onGoalReached`, and `onGameOver` in the Inspector. Reaching the goal automatically displays YOU WIN; the target can also be changed using the game's `+` / `-` UI. Goals changed in game persist through `PlayerPrefs` and can override the Inspector default on later runs.

## How inputs are read

The Uno emits complete lines `S,<jump>,<duck>`, with 0 = released and 1 = held, at 115200 baud. The Unity component `DinoPadUnoSerial.cs` opens that COM/serial port, reads data on a worker thread, and applies presses on the Unity main thread. Jump occurs once on the transition to pressed; Duck stays active for as long as the pad is held. The Uno uses 25ms debounce. The serial reader releases held inputs on disconnect and reconnects automatically.

## Troubleshooting

- **C# error: `System.IO.Ports` missing**: Change API Compatibility Level to `.NET Framework`. Check you're using the desktop build target.
- **Port access denied**: Close Arduino IDE Serial Monitor/Serial Plotter and other serial apps.
- **Multiple ports / AUTO not finding your Uno**: Enter your board's exact port in `Port Name`.
- **Never sees Connected**: Verify correct firmware, correct port, 115200 baud, USB data cable; check Serial Monitor first.
- **Connection works, pads don't**: Temporarily short D2 to GND for Jump and D3 to GND for Duck.
- **Mac/Linux permission errors**: Verify OS serial-device permissions.
- **Keyboard-only test**: Set `Connect On Start` off in Inspector.

## Files

- `Scripts/DinoPadGame.cs`: complete game, on-screen score and winning goal
- `Scripts/DinoPadUnoSerial.cs`: Arduino Uno USB serial input for Unity
- `Arduino/DinoPad_Uno.ino`: firmware to upload to Arduino Uno
- `Scenes/DinoPadGame.unity`: preconfigured sample game scene
- `Editor/DinoPadSceneTools.cs`: optional Unity menu commands

Static package consistency checks completed, but Unity Editor execution and physical Uno tests **have not** been performed here.
