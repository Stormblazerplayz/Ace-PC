/*
  BUTTON TURNS AN LED ON AND OFF  (Arduino, with debounce)
  Press the button once = LED on. Press again = LED off.

  WIRING:
  - Push button: one leg -> pin 2, the other leg -> GND.
    (We use the Arduino's built-in "pull-up" resistor, so no extra resistor needed!)
  - LED: long leg (+) -> 220 ohm resistor -> pin 13. Short leg (-) -> GND.
    (Or just watch the built-in LED on pin 13.)

  WHAT IS DEBOUNCE?
  Inside a button, the metal bounces for a few milliseconds when you press it,
  so the Arduino might see "pressed, not pressed, pressed" super fast.
  Debounce means: wait until the button has been steady for a moment before believing it.
*/
#include <Arduino.h>

const int BUTTON_PIN = 2;
const int LED_PIN = 13;
const unsigned long DEBOUNCE_MS = 50;  // how long the button must be steady

bool ledOn = false;
int stableState = HIGH;        // with INPUT_PULLUP: HIGH = not pressed, LOW = pressed
int lastReading = HIGH;
unsigned long lastChangeTime = 0;

void setup() {
  pinMode(BUTTON_PIN, INPUT_PULLUP);
  pinMode(LED_PIN, OUTPUT);
  digitalWrite(LED_PIN, LOW);
  Serial.begin(9600);
  Serial.println("Press the button!");
}

void loop() {
  int reading = digitalRead(BUTTON_PIN);

  // If the reading changed, start the debounce timer again
  if (reading != lastReading) {
    lastChangeTime = millis();
    lastReading = reading;
  }

  // If it has stayed the same long enough, it's a real change
  if (millis() - lastChangeTime > DEBOUNCE_MS && reading != stableState) {
    stableState = reading;

    // Only toggle when the button goes DOWN (LOW), not when it comes back up
    if (stableState == LOW) {
      ledOn = !ledOn;
      digitalWrite(LED_PIN, ledOn ? HIGH : LOW);
      Serial.println(ledOn ? "LED ON" : "LED OFF");
    }
  }
}
