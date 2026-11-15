/*
  BLINK AN LED  (Arduino)
  The "Hello World" of Arduino: makes an LED turn on and off forever.

  WIRING:
  - Nothing needed! Most boards (Uno, Nano, Mega) have a tiny LED built in on pin 13.
  - Want a bigger LED? Long leg (+) -> 220 ohm resistor -> pin 13. Short leg (-) -> GND.

  HOW TO USE: Paste into the Arduino IDE, pick your board and port, press Upload.
*/
#include <Arduino.h>

const int LED_PIN = LED_BUILTIN;  // pin 13 on most boards
const int ON_TIME = 500;          // milliseconds the LED stays ON  (1000 ms = 1 second)
const int OFF_TIME = 500;         // milliseconds the LED stays OFF

void setup() {
  pinMode(LED_PIN, OUTPUT);  // tell the Arduino this pin sends power OUT
}

void loop() {
  digitalWrite(LED_PIN, HIGH);  // HIGH = power on = LED on
  delay(ON_TIME);
  digitalWrite(LED_PIN, LOW);   // LOW = power off = LED off
  delay(OFF_TIME);
}
