/*
  HELLO + INPUT BASICS  (Console C++)
  Your first C++ program: prints text, asks questions, stores answers in variables,
  and does a little math with them.

  HOW TO RUN:
  - Online: paste into https://www.onlinegdb.com (pick C++17) and press Run.
  - On your PC: g++ -std=c++17 hello-input.cpp -o hello   then run ./hello
*/
#include <iostream>  // lets us use std::cout (print) and std::cin (read)
#include <limits>    // for std::numeric_limits (used to clear bad input)
#include <string>    // lets us use std::string (text)

int main() {
    // std::cout prints. "\n" means "new line".
    std::cout << "Hello, world! I'm your first C++ program.\n\n";

    // A string holds text. getline reads a whole line, even with spaces.
    std::string name;
    std::cout << "What's your name? ";
    std::getline(std::cin, name);
    if (name.empty()) {
        name = "Mystery Player";
    }

    // An int holds a whole number.
    int age = 0;
    std::cout << "Hi " << name << "! How old are you? ";
    while (!(std::cin >> age) || age < 0 || age > 150) {
        if (std::cin.eof()) {
            std::cout << "\nBye!\n";
            return 0;
        }
        std::cin.clear();  // reset the error
        std::cin.ignore(std::numeric_limits<std::streamsize>::max(), '\n');  // throw away the bad typing
        std::cout << "Please type your age as a number: ";
    }
    std::cin.ignore(std::numeric_limits<std::streamsize>::max(), '\n');  // eat the Enter key

    std::string game;
    std::cout << "What's your favorite game? ";
    std::getline(std::cin, game);
    if (game.empty()) {
        game = "Roblox";
    }

    // Math with variables
    int nextYear = age + 1;
    int dogYears = age * 7;
    double hoursSlept = age * 365.0 * 9.0;  // about 9 hours of sleep a night

    std::cout << "\n--- About you ---\n";
    std::cout << "Name: " << name << "\n";
    std::cout << "Next year you'll be " << nextYear << ".\n";
    std::cout << "In dog years you are " << dogYears << "!\n";
    std::cout << "You've slept about " << static_cast<long long>(hoursSlept) << " hours in your life.\n";

    // if / else: making decisions
    if (age < 13) {
        std::cout << "You're starting to code young. Awesome!\n";
    } else {
        std::cout << "It's never too late to learn to code!\n";
    }

    std::cout << "Have fun playing " << game << ", " << name << "!\n";
    return 0;  // 0 means "the program finished OK"
}
