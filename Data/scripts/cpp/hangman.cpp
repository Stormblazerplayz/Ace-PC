/*
  HANGMAN  (Console C++)
  Guess the secret word one letter at a time before the drawing is finished!
  You get 6 wrong guesses.

  HOW TO RUN: g++ -std=c++17 hangman.cpp -o hangman   then ./hangman
  Add your own words to the WORDS list (lowercase letters only).
*/
#include <cctype>
#include <iostream>
#include <random>
#include <string>
#include <vector>

const std::vector<std::string> WORDS = {
    "roblox", "arduino", "keyboard", "dragon", "volcano", "spaceship", "pizza",
    "robot", "minecraft", "galaxy", "treasure", "lightning", "penguin", "castle"
};

// The drawing for 0 to 6 wrong guesses
const std::string PICTURES[7] = {
    "  +---+\n  |   |\n      |\n      |\n      |\n      |\n=========\n",
    "  +---+\n  |   |\n  O   |\n      |\n      |\n      |\n=========\n",
    "  +---+\n  |   |\n  O   |\n  |   |\n      |\n      |\n=========\n",
    "  +---+\n  |   |\n  O   |\n /|   |\n      |\n      |\n=========\n",
    "  +---+\n  |   |\n  O   |\n /|\\  |\n      |\n      |\n=========\n",
    "  +---+\n  |   |\n  O   |\n /|\\  |\n /    |\n      |\n=========\n",
    "  +---+\n  |   |\n  O   |\n /|\\  |\n / \\  |\n      |\n=========\n"
};

int main() {
    const int MAX_WRONG = 6;

    std::random_device seed;
    std::mt19937 rng(seed());
    std::uniform_int_distribution<std::size_t> pickWord(0, WORDS.size() - 1);

    const std::string secret = WORDS[pickWord(rng)];
    std::string shown(secret.size(), '_');  // starts as "_____"
    std::string guessedLetters;
    int wrong = 0;

    std::cout << "=== HANGMAN ===\n";

    while (wrong < MAX_WRONG && shown != secret) {
        std::cout << "\n" << PICTURES[wrong];
        std::cout << "Word: ";
        for (char c : shown) {
            std::cout << c << ' ';
        }
        std::cout << "\nGuessed so far: " << (guessedLetters.empty() ? "(none)" : guessedLetters) << "\n";
        std::cout << "Wrong guesses left: " << (MAX_WRONG - wrong) << "\n";
        std::cout << "Guess a letter: ";

        std::string input;
        if (!(std::cin >> input)) {
            std::cout << "\nBye! The word was: " << secret << "\n";
            return 0;
        }

        char letter = static_cast<char>(std::tolower(static_cast<unsigned char>(input[0])));
        if (!std::isalpha(static_cast<unsigned char>(letter))) {
            std::cout << "Letters only!\n";
            continue;
        }
        if (guessedLetters.find(letter) != std::string::npos) {
            std::cout << "You already guessed '" << letter << "'!\n";
            continue;
        }
        guessedLetters += letter;

        bool found = false;
        for (std::size_t i = 0; i < secret.size(); i++) {
            if (secret[i] == letter) {
                shown[i] = letter;  // reveal the letter
                found = true;
            }
        }

        if (found) {
            std::cout << "Yes! '" << letter << "' is in the word.\n";
        } else {
            wrong++;
            std::cout << "Nope, no '" << letter << "'.\n";
        }
    }

    std::cout << "\n" << PICTURES[wrong];
    if (shown == secret) {
        std::cout << "YOU WIN! The word was \"" << secret << "\" and you had "
                  << (MAX_WRONG - wrong) << " guesses left.\n";
    } else {
        std::cout << "Game over! The word was \"" << secret << "\".\n";
    }
    return 0;
}
