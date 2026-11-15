/*
  DC MOTOR WITH AN L298N DRIVER  (Arduino)
  Controls a motor's SPEED and DIRECTION: speeds up forward, stops, then goes backward.
  Watch the Serial Monitor (9600 baud) to see what it is doing.

  WIRING (motor A side of the L298N):
  - Motor wires          -> OUT1 and OUT2
  - Battery pack + (6-12V) -> L298N "12V" pin
  - Battery pack -       -> L298N GND  AND  Arduino GND (the grounds MUST be connected!)
  - L298N ENA            -> pin 9  (remove the little ENA jumper cap first, this pin controls speed)
  - L298N IN1            -> pin 8
  - L298N IN2            -> pin 7
  Never power the motor from the Arduino's 5V pin - motors need their own batteries.
*/
#include <Arduino.h>

const int ENA_PIN = 9;  // speed (must be a PWM pin, marked with ~)
const int IN1_PIN = 8;  // direction
const int IN2_PIN = 7;  // direction

// speed goes from -255 (full backward) to 255 (full forward). 0 = stop.
void setMotor(int speed) {
  speed = constrain(speed, -255, 255);
  if (speed > 0) {
    digitalWrite(IN1_PIN, HIGH);  // forward
    digitalWrite(IN2_PIN, LOW);
  } else if (speed < 0) {
    digitalWrite(IN1_PIN, LOW);   // backward
    digitalWrite(IN2_PIN, HIGH);
  } else {
    digitalWrite(IN1_PIN, LOW);   // stop
    digitalWrite(IN2_PIN, LOW);
  }
  analogWrite(ENA_PIN, abs(speed));  // how much power (0-255)
}

void setup() {
  pinMode(ENA_PIN, OUTPUT);
  pinMode(IN1_PIN, OUTPUT);
  pinMode(IN2_PIN, OUTPUT);
  setMotor(0);
  Serial.begin(9600);
}

void loop() {
  Serial.println("Speeding up forward");
  for (int speed = 0; speed <= 255; speed += 5) {
    setMotor(speed);
    delay(30);
  }
  delay(1000);

  Serial.println("Stop");
  setMotor(0);
  delay(1000);

  Serial.println("Backward at half speed");
  setMotor(-128);
  delay(2000);

  Serial.println("Stop");
  setMotor(0);
  delay(1000);
}
