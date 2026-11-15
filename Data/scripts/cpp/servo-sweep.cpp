/*
  SERVO SWEEP  (Arduino)
  Makes a servo motor slowly turn from 0 to 180 degrees and back, like a windshield wiper.

  WIRING (small SG90 / MG90S servo):
  - Brown or Black wire  -> GND
  - Red wire             -> 5V
  - Orange or Yellow wire (signal) -> pin 9
  Tip: bigger servos need their own power supply (connect its GND to Arduino GND too).

  LIBRARY: "Servo" comes with the Arduino IDE already.
*/
#include <Arduino.h>
#include <Servo.h>

const int SERVO_PIN = 9;
const int STEP_DELAY = 15;  // milliseconds between each 1-degree step (bigger = slower)

Servo myServo;

void setup() {
  myServo.attach(SERVO_PIN);
  myServo.write(90);  // start in the middle
  delay(500);
}

void loop() {
  // Turn from 0 up to 180 degrees
  for (int angle = 0; angle <= 180; angle++) {
    myServo.write(angle);
    delay(STEP_DELAY);
  }
  // Turn back from 180 down to 0 degrees
  for (int angle = 180; angle >= 0; angle--) {
    myServo.write(angle);
    delay(STEP_DELAY);
  }
}
