/*
  JOYSTICK READER  (Arduino)
  Reads a thumb joystick (like on a game controller) and prints which way you push it,
  plus when you click it down. Open the Serial Monitor at 9600 baud.

  WIRING (common KY-023 joystick module):
  - GND -> GND
  - +5V -> 5V
  - VRx -> A0   (left / right)
  - VRy -> A1   (up / down)
  - SW  -> pin 2 (the click button)
*/
#include <Arduino.h>

const int X_PIN = A0;
const int Y_PIN = A1;
const int BUTTON_PIN = 2;
const int DEAD_ZONE = 150;  // ignore small wiggles around the middle (the middle is about 512)

void setup() {
  pinMode(BUTTON_PIN, INPUT_PULLUP);  // the button connects to GND when clicked
  Serial.begin(9600);
}

void loop() {
  int x = analogRead(X_PIN);  // 0 to 1023
  int y = analogRead(Y_PIN);  // 0 to 1023
  bool clicked = digitalRead(BUTTON_PIN) == LOW;

  // Turn the numbers into words
  const char* horizontal = "";
  const char* vertical = "";
  if (x < 512 - DEAD_ZONE) {
    horizontal = "LEFT ";
  } else if (x > 512 + DEAD_ZONE) {
    horizontal = "RIGHT ";
  }
  if (y < 512 - DEAD_ZONE) {
    vertical = "UP ";
  } else if (y > 512 + DEAD_ZONE) {
    vertical = "DOWN ";
  }

  // Also make -100 to 100 numbers (easier to use for steering a robot!)
  int xPercent = map(x, 0, 1023, -100, 100);
  int yPercent = map(y, 0, 1023, -100, 100);

  Serial.print("X: ");
  Serial.print(xPercent);
  Serial.print("%  Y: ");
  Serial.print(yPercent);
  Serial.print("%  ");
  Serial.print(vertical);
  Serial.print(horizontal);
  if (clicked) {
    Serial.print("CLICK!");
  }
  Serial.println();

  delay(100);
}
