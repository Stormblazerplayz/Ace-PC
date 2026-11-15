/*
  POTENTIOMETER LED DIMMER  (Arduino)
  Turn the knob to make an LED brighter or dimmer.

  WIRING:
  - Potentiometer (the knob): left leg -> 5V, middle leg -> A0, right leg -> GND
  - LED: long leg (+) -> 220 ohm resistor -> pin 9, short leg (-) -> GND
    (pin 9 has a ~ next to it, which means it can do "PWM" fake-dimming)
*/
#include <Arduino.h>

const int KNOB_PIN = A0;
const int LED_PIN = 9;  // must be a ~ (PWM) pin: 3, 5, 6, 9, 10 or 11 on an Uno

void setup() {
  pinMode(LED_PIN, OUTPUT);
  Serial.begin(9600);
}

void loop() {
  int knob = analogRead(KNOB_PIN);               // 0 to 1023
  int brightness = map(knob, 0, 1023, 0, 255);   // analogWrite wants 0 to 255
  analogWrite(LED_PIN, brightness);

  Serial.print("Knob: ");
  Serial.print(knob);
  Serial.print("  Brightness: ");
  Serial.println(brightness);

  delay(50);
}
