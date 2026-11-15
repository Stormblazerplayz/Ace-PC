/*
  LED STRIP RAINBOW (NeoPixel / WS2812B)  (Arduino)
  Makes a smooth rainbow that slides along an addressable LED strip.

  WIRING:
  - Strip 5V (+)  -> 5V  (for more than ~15 LEDs, use a separate 5V power supply)
  - Strip GND (-) -> GND (connect the power supply GND to Arduino GND too)
  - Strip DIN     -> 330 ohm resistor -> pin 6
  Tip: a big capacitor (1000 uF) across 5V and GND near the strip protects it.

  LIBRARY: In the Arduino IDE go to Tools > Manage Libraries,
  search "Adafruit NeoPixel" and click Install.
*/
#include <Arduino.h>
#include <Adafruit_NeoPixel.h>

const int LED_PIN = 6;
const int LED_COUNT = 30;       // how many LEDs are on your strip
const int BRIGHTNESS = 50;      // 0-255. Keep it low to save power (and your eyes!)
const int SPEED_DELAY = 10;     // milliseconds between frames (smaller = faster)

Adafruit_NeoPixel strip(LED_COUNT, LED_PIN, NEO_GRB + NEO_KHZ800);

// firstHue moves the whole rainbow along. 65536 = one full trip around the color wheel.
uint16_t firstHue = 0;

void setup() {
  strip.begin();
  strip.setBrightness(BRIGHTNESS);
  strip.show();  // start with all LEDs off
}

void loop() {
  for (uint16_t i = 0; i < strip.numPixels(); i++) {
    // Spread one whole rainbow across the strip
    uint16_t pixelHue = firstHue + (uint16_t)((uint32_t)i * 65536UL / strip.numPixels());
    // ColorHSV makes a color from the color wheel; gamma32 makes it look nicer to our eyes
    strip.setPixelColor(i, strip.gamma32(strip.ColorHSV(pixelHue)));
  }
  strip.show();  // send the colors to the strip

  firstHue += 256;  // move the rainbow a little (uint16_t wraps back to 0 after 65535)
  delay(SPEED_DELAY);
}
