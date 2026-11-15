/*
  BUZZER MELODY  (Arduino)
  Plays "Twinkle Twinkle Little Star" on a buzzer using tone().
  Press the button to play it again.

  WIRING:
  - PASSIVE buzzer (or small speaker): + leg -> pin 8, - leg -> GND
    (Passive buzzers can play different notes. Active buzzers can only beep.)
  - (Optional) Push button: one leg -> pin 2, other leg -> GND.
*/
#include <Arduino.h>

const int BUZZER_PIN = 8;
const int BUTTON_PIN = 2;

// Note frequencies (how many times per second the buzzer wiggles)
const int NOTE_C4 = 262;
const int NOTE_D4 = 294;
const int NOTE_E4 = 330;
const int NOTE_F4 = 349;
const int NOTE_G4 = 392;
const int NOTE_A4 = 440;
const int REST = 0;  // silence

// The song: each note, then how long it lasts (4 = quarter note, 2 = half note)
const int melody[] = {
  NOTE_C4, NOTE_C4, NOTE_G4, NOTE_G4, NOTE_A4, NOTE_A4, NOTE_G4, REST,
  NOTE_F4, NOTE_F4, NOTE_E4, NOTE_E4, NOTE_D4, NOTE_D4, NOTE_C4, REST
};
const int noteLengths[] = {
  4, 4, 4, 4, 4, 4, 2, 4,
  4, 4, 4, 4, 4, 4, 2, 4
};
const int NOTE_COUNT = sizeof(melody) / sizeof(melody[0]);
const int WHOLE_NOTE_MS = 1600;  // smaller = faster song

void playSong() {
  for (int i = 0; i < NOTE_COUNT; i++) {
    int duration = WHOLE_NOTE_MS / noteLengths[i];
    if (melody[i] != REST) {
      tone(BUZZER_PIN, melody[i], duration);
    }
    // Wait a bit longer than the note so the notes don't blur together
    delay(duration * 13 / 10);
    noTone(BUZZER_PIN);
  }
}

void setup() {
  pinMode(BUTTON_PIN, INPUT_PULLUP);
  playSong();  // play once when it starts
}

void loop() {
  if (digitalRead(BUTTON_PIN) == LOW) {  // button pressed
    playSong();
  }
}
