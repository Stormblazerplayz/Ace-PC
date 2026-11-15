/*
  NUMBER GUESSING GAME  (Console C++)
  The computer picks a secret number from 1 to 100. You guess, and it tells you
  "too high" or "too low" until you get it. Then you can play again!

  HOW TO RUN: g++ -std=c++17 guess-number.cpp -o guess   then ./guess
*/
#include <iostream>
#include <limits>
#include <random>
#include <string>

// Reads a whole number from the keyboard. Returns false if the input has ended.
bool readNumber(int& number) {
    while (true) {
        if (std::cin >> number) {
            return true;
        }
        if (std::cin.eof()) {
            return false;
        }
        std::cin.clear();
        std::cin.ignore(std::numeric_limits<std::streamsize>::max(), '\n');
        std::cout << "That's not a number. Try again: ";
    }
}

int main() {
    // A random number generator, seeded from the computer's random device
    std::random_device seed;
    std::mt19937 rng(seed());

    const int LOWEST = 1;
    const int HIGHEST = 100;
    int bestScore = 0;  // fewest guesses so far (0 = no games yet)

    std::cout << "=== GUESS THE NUMBER ===\n";

    bool keepPlaying = true;
    while (keepPlaying) {
        std::uniform_int_distribution<int> pick(LOWEST, HIGHEST);
        int secret = pick(rng);
        int guesses = 0;
        std::cout << "\nI'm thinking of a number from " << LOWEST << " to " << HIGHEST << ".\n";

        while (true) {
            std::cout << "Your guess: ";
            int guess = 0;
            if (!readNumber(guess)) {
                std::cout << "\nBye!\n";
                return 0;
            }
            if (guess < LOWEST || guess > HIGHEST) {
                std::cout << "Pick a number from " << LOWEST << " to " << HIGHEST << "!\n";
                continue;
            }
            guesses++;

            if (guess < secret) {
                std::cout << "Too low!\n";
            } else if (guess > secret) {
                std::cout << "Too high!\n";
            } else {
                std::cout << "YOU GOT IT! The number was " << secret << ". It took you "
                          << guesses << (guesses == 1 ? " guess.\n" : " guesses.\n");
                if (bestScore == 0 || guesses < bestScore) {
                    bestScore = guesses;
                    std::cout << "That's a new best score!\n";
                }
                break;
            }
        }

        std::cout << "Play again? (y/n): ";
        std::string answer;
        if (!(std::cin >> answer)) {
            break;
        }
        keepPlaying = !answer.empty() && (answer[0] == 'y' || answer[0] == 'Y');
    }

    std::cout << "Thanks for playing! Best score: " << bestScore << " guesses.\n";
    return 0;
}
