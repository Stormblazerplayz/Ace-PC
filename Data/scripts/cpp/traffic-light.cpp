/*
  TRAFFIC LIGHT  (Arduino)
  Red -> Green -> Yellow -> Red, just like a real traffic light.
  Bonus: press the "walk" button and the light turns red sooner so people can cross.

  WIRING (each LED: long leg -> 220 ohm resistor -> pin, short leg -> GND):
  - Red LED    -> pin 12
  - Yellow LED -> pin 11
  - Green LED  -> pin 10
  - (Optional) Walk button: one leg -> pin 2, other leg -> GND
*/
#include <Arduino.h>

const int RED_PIN = 12;
const int YELLOW_PIN = 11;
const int GREEN_PIN = 10;
const int WALK_BUTTON_PIN = 2;

const unsigned long RED_TIME = 5000;     // milliseconds
const unsigned long GREEN_TIME = 6000;
const unsigned long YELLOW_TIME = 2000;

void showLight(bool red, bool yellow, bool green) {
  digitalWrite(RED_PIN, red ? HIGH : LOW);
  digitalWrite(YELLOW_PIN, yellow ? HIGH : LOW);
  digitalWrite(GREEN_PIN, green ? HIGH : LOW);
}

// Waits for "ms" milliseconds, but stops early if the walk button is pressed
void waitOrWalk(unsigned long ms) {
  unsigned long start = millis();
  while (millis() - start < ms) {
    if (digitalRead(WALK_BUTTON_PIN) == LOW) {
      delay(500);  // a short moment, then change the light
      return;
    }
    delay(10);
  }
}

void setup() {
  pinMode(RED_PIN, OUTPUT);
  pinMode(YELLOW_PIN, OUTPUT);
  pinMode(GREEN_PIN, OUTPUT);
  pinMode(WALK_BUTTON_PIN, INPUT_PULLUP);
}

void loop() {
  showLight(true, false, false);   // RED: stop
  delay(RED_TIME);

  showLight(false, false, true);   // GREEN: go (the walk button can end it early)
  waitOrWalk(GREEN_TIME);

  showLight(false, true, false);   // YELLOW: slow down
  delay(YELLOW_TIME);
}
