#include <SoftwareSerial.h>

// HC-05: TXD -> D2 (Arduino RX), RXD -> D3 (Arduino TX) (kříženě)
// Pozor: HC-05 RXD je 3.3V logika, na Arduino TX (D3) dej odporový dělič!
SoftwareSerial bt(2, 3);

const int PINS[5] = {A0, A1, A2, A3, A4};
int lastVals[5] = { -1, -1, -1, -1, -1 };

int readSmoothed(int pin) {
  long sum = 0;
  for (int i = 0; i < 5; i++) {
    sum += analogRead(pin);
    delayMicroseconds(600);
  }
  return (int)(sum / 5);
}

void sendFrame(Stream &out, int v0, int v1, int v2, int v3, int v4) {
  out.print("S,");
  out.print(v0); out.print(",");
  out.print(v1); out.print(",");
  out.print(v2); out.print(",");
  out.print(v3); out.print(",");
  out.print(v4);
  out.print("\n");
}

// --------------------
// Failover logic:
// Arduino každou sekundu pošle na USB "PINGPC".
// PC aplikace odpoví "PONGPC".
// Když 3× za sebou nepřijde PONGPC -> přepne se na BT režim (posílá data jen přes BT).
// Jakmile PONGPC zase chodí -> přepne zpět na USB.
// --------------------
unsigned long lastPingMs = 0;
bool usbPongSinceLastPing = false;
int usbMisses = 0;
bool usbOk = true;

void handleIncoming(Stream &in, Stream &out, bool isUsb) {
  if (!in.available()) return;

  String line = in.readStringUntil('\n');
  line.trim();

  // PC handshake (z appky / při hledání portu)
  if (line.equalsIgnoreCase("PING")) {
    out.print("PONG,VC5\n");
    return;
  }

  // Odpověď PC na náš ping
  if (isUsb && line.equalsIgnoreCase("PONGPC")) {
    usbPongSinceLastPing = true;
    return;
  }
}

void setup() {
  Serial.begin(115200); // USB
  bt.begin(9600);       // HC-05
}

void loop() {
  // Čti příchozí řádky (USB + BT)
  handleIncoming(Serial, Serial, true);
  handleIncoming(bt, bt, false);

  // Ping PC po USB každou sekundu
  unsigned long now = millis();
  if (now - lastPingMs >= 1000) {
    lastPingMs = now;

    Serial.print("PINGPC\n");

    if (usbPongSinceLastPing) {
      usbMisses = 0;
    } else {
      usbMisses++;
    }
    usbPongSinceLastPing = false;

    usbOk = (usbMisses < 3);
  }

  int vals[5];
  bool changed = false;

  for (int i = 0; i < 5; i++) {
    vals[i] = readSmoothed(PINS[i]);
    if (lastVals[i] == -1 || abs(vals[i] - lastVals[i]) >= 4) changed = true;
  }

  if (changed) {
    if (usbOk) {
      sendFrame(Serial, vals[0], vals[1], vals[2], vals[3], vals[4]);
    } else {
      sendFrame(bt, vals[0], vals[1], vals[2], vals[3], vals[4]);
    }

    for (int i = 0; i < 5; i++) lastVals[i] = vals[i];
  }

  delay(20); // ~50 Hz
}
