const byte JUMP_PIN = 2;
const byte DUCK_PIN = 3;
const byte TEST_1 = 4;
const byte TEST_2 = 5;

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

  bool active = digitalRead(pad.pin) == LOW;
  pad.lastRaw = active;
  pad.pressed = active;
  pad.changedAt = millis();
}

bool updatePad(Pad &pad, unsigned long now) {
  bool raw = digitalRead(pad.pin) == LOW;

  if (raw != pad.lastRaw) {
    pad.changedAt = now;
    pad.lastRaw = raw;
  }

  if (now - pad.changedAt >= DEBOUNCE_MS &&
      raw != pad.pressed) {
    pad.pressed = raw;
    return true;
  }

  return false;
}

void sendState() {
  Serial.print("S,");
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
