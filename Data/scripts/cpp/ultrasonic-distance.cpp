/*
  ULTRASONIC DISTANCE SENSOR (HC-SR04)  (Arduino)
  Measures how far away something is, like a bat using echoes!
  Open Tools > Serial Monitor (9600 baud) to see the distance in centimeters.

  WIRING:
  - VCC  -> 5V
  - Trig -> pin 9
  - Echo -> pin 10
  - GND  -> GND
  (Optional) LED + 220 ohm resistor on pin 13: lights up when something is closer than 20 cm.

  HOW IT WORKS:
  We send a short "ping" of sound, then time how long the echo takes to come back.
  Sound travels about 0.0343 cm every microsecond. The sound goes there AND back,
  so we divide by 2.
*/
#include <Arduino.h>

const int TRIG_PIN = 9;
const int ECHO_PIN = 10;
const int LED_PIN = 13;
const float TOO_CLOSE_CM = 20.0;

void setup() {
  pinMode(TRIG_PIN, OUTPUT);
  pinMode(ECHO_PIN, INPUT);
  pinMode(LED_PIN, OUTPUT);
  digitalWrite(TRIG_PIN, LOW);
  Serial.begin(9600);
}

float readDistanceCm() {
  // Send a 10 microsecond ping
  digitalWrite(TRIG_PIN, LOW);
  delayMicroseconds(2);
  digitalWrite(TRIG_PIN, HIGH);
  delayMicroseconds(10);
  digitalWrite(TRIG_PIN, LOW);

  // Wait for the echo (give up after 30000 microseconds = about 5 meters)
  unsigned long echoTime = pulseIn(ECHO_PIN, HIGH, 30000UL);
  if (echoTime == 0) {
    return -1.0;  // no echo came back (nothing in front, or too far)
  }
  return echoTime * 0.0343 / 2.0;
}

void loop() {
  float distance = readDistanceCm();

  if (distance < 0) {
    Serial.println("Nothing detected (out of range)");
    digitalWrite(LED_PIN, LOW);
  } else {
    Serial.print("Distance: ");
    Serial.print(distance, 1);  // 1 number after the dot
    Serial.println(" cm");
    digitalWrite(LED_PIN, distance < TOO_CLOSE_CM ? HIGH : LOW);
  }

  delay(200);  // measure 5 times per second
}
