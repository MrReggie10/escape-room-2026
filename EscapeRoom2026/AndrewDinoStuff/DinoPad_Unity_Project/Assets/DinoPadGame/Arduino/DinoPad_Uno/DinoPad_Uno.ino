// Dino Pad - Arduino Uno R3 / compatible serial input
// Wire JUMP switch between pin D2 and GND.
// Wire DUCK switch between pin D3 and GND.
// No external pullup resistors are necessary.
// Runs at 115200 baud: matches DinoPadUnoSerial in Unity.
// Packet: S,J,D\n where J and D are 0 (released) or 1 (pressed).
// Sends when state changes AND periodically so Unity can reconnect safely.

const byte JUMP_PIN = 2;
const byte DUCK_PIN = 3;
const unsigned long DEBOUNCE_MS = 25;
const unsigned long HEARTBEAT_MS = 120;

struct Pad {
  byte pin;
  bool lastRaw;
  bool pressed;
  unsigned long changedAt;
};

Pad jumpPad = {JUMP_PIN, false, false, 0};
Pad duckPad = {DUCK_PIN, false, false, 0};
unsigned long lastSend = 0;

void initPad(Pad &pad) {
  pinMode(pad.pin, INPUT_PULLUP);
  bool active = (digitalRead(pad.pin) == LOW);
  pad.lastRaw = active;
  pad.pressed = active;
  pad.changedAt = millis();
}

bool updatePad(Pad &pad, unsigned long now) {
  bool raw = (digitalRead(pad.pin) == LOW);
  if (raw != pad.lastRaw) {
    pad.changedAt = now;
    pad.lastRaw = raw;
  }
  if (now - pad.changedAt >= DEBOUNCE_MS && raw != pad.pressed) {
    pad.pressed = raw;
    return true;
  }
  return false;
}

void sendState() {
  Serial.print(F("S,"));
  Serial.print(jumpPad.pressed ? '1' : '0');
  Serial.print(',');
  Serial.println(duckPad.pressed ? '1' : '0');
}

void setup() {
  initPad(jumpPad);
  initPad(duckPad);
  Serial.begin(115200);
  sendState();
  lastSend = millis();
}

void loop() {
  unsigned long now = millis();
  bool changed = updatePad(jumpPad, now);
  changed = updatePad(duckPad, now) || changed;
  if (changed || now - lastSend >= HEARTBEAT_MS) {
    sendState();
    lastSend = now;
  }
}
