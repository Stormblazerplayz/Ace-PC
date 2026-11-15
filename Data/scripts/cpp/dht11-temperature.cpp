/*
  TEMPERATURE + HUMIDITY WITH A DHT11  (Arduino)
  Reads the temperature and how wet the air is, every 2 seconds.
  Open the Serial Monitor at 9600 baud. Also turns on an LED (a "fan" warning) when it's hot.

  WIRING (3-pin DHT11 module):
  - + (VCC)   -> 5V
  - - (GND)   -> GND
  - OUT/DATA  -> pin 2
  (Bare 4-pin DHT11: also put a 10k resistor between DATA and 5V.)
  - (Optional) LED + 220 ohm resistor on pin 13 for the "too hot" light.

  LIBRARIES: Tools > Manage Libraries, install "DHT sensor library" by Adafruit
  (say YES when it asks to also install "Adafruit Unified Sensor").
*/
#include <Arduino.h>
#include <DHT.h>

const int DHT_PIN = 2;
const int HOT_LED_PIN = 13;
const float HOT_CELSIUS = 28.0;  // LED turns on above this temperature

DHT dht(DHT_PIN, DHT11);  // change DHT11 to DHT22 if you have the white DHT22 sensor

void setup() {
  pinMode(HOT_LED_PIN, OUTPUT);
  Serial.begin(9600);
  dht.begin();
  Serial.println("DHT11 starting");
}

void loop() {
  delay(2000);  // the DHT11 is slow: only read every 2 seconds

  float humidity = dht.readHumidity();
  float celsius = dht.readTemperature();         // Celsius
  float fahrenheit = dht.readTemperature(true);  // true = Fahrenheit

  // isnan means "is not a number" = the reading failed
  if (isnan(humidity) || isnan(celsius) || isnan(fahrenheit)) {
    Serial.println("Could not read the sensor. Check the wires!");
    return;
  }

  Serial.print("Temperature: ");
  Serial.print(celsius, 1);
  Serial.print(" C / ");
  Serial.print(fahrenheit, 1);
  Serial.print(" F   Humidity: ");
  Serial.print(humidity, 0);
  Serial.println(" %");

  digitalWrite(HOT_LED_PIN, celsius > HOT_CELSIUS ? HIGH : LOW);
}
