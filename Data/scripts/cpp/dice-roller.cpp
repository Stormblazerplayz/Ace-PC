/*
  DICE ROLLER  (Console C++)
  Roll any number of dice with any number of sides, using C++'s <random> library.
  Type rolls like:  2d6  (two 6-sided dice)   1d20   3d4   or just press Enter for 1d6.
  Type "stats" to roll 2d6 one thousand times and see which totals happen most.
  Type q to quit.

  HOW TO RUN: g++ -std=c++17 dice-roller.cpp -o dice   then ./dice
*/
#include <iostream>
#include <random>
#include <sstream>
#include <string>
#include <vector>

// Reads text like "2d6" into count = 2, sides = 6. Returns false if it doesn't make sense.
bool parseDice(const std::string& text, int& count, int& sides) {
    std::istringstream stream(text);
    char d = 0;
    if (!(stream >> count >> d >> sides) || (d != 'd' && d != 'D')) {
        return false;
    }
    std::string leftover;
    if (stream >> leftover) {
        return false;
    }
    return count >= 1 && count <= 100 && sides >= 2 && sides <= 1000;
}

int main() {
    std::random_device seed;
    std::mt19937 rng(seed());

    std::cout << "=== DICE ROLLER ===\n";
    std::cout << "Type something like 2d6, 1d20, \"stats\", or q to quit.\n";

    std::string line;
    while (true) {
        std::cout << "\nRoll: ";
        if (!std::getline(std::cin, line) || line == "q" || line == "Q") {
            break;
        }

        if (line == "stats") {
            // Roll two dice 1000 times and count each total (2 to 12)
            std::uniform_int_distribution<int> die(1, 6);
            std::vector<int> counts(13, 0);
            for (int i = 0; i < 1000; i++) {
                counts[die(rng) + die(rng)]++;
            }
            std::cout << "2d6 rolled 1000 times (each # is 5 rolls):\n";
            for (int total = 2; total <= 12; total++) {
                std::cout << (total < 10 ? " " : "") << total << ": " << std::string(counts[total] / 5, '#')
                          << " " << counts[total] << "\n";
            }
            std::cout << "See? 7 happens the most, because the most dice combos add up to 7!\n";
            continue;
        }

        int count = 1;
        int sides = 6;
        if (!line.empty() && !parseDice(line, count, sides)) {
            std::cout << "Try something like 2d6 (1-100 dice, 2-1000 sides).\n";
            continue;
        }

        std::uniform_int_distribution<int> die(1, sides);
        int total = 0;
        std::cout << "Rolling " << count << "d" << sides << ": ";
        for (int i = 0; i < count; i++) {
            int roll = die(rng);
            total += roll;
            std::cout << roll << (i + 1 < count ? " + " : "");
        }
        if (count > 1) {
            std::cout << " = " << total;
        }
        std::cout << "\n";

        if (sides == 20 && count == 1) {
            if (total == 20) {
                std::cout << "NATURAL 20! Critical hit!\n";
            } else if (total == 1) {
                std::cout << "Natural 1. Critical fail!\n";
            }
        }
    }

    std::cout << "Bye!\n";
    return 0;
}
