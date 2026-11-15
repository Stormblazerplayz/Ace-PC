/*
  TIMES TABLE QUIZ  (Console C++)
  Practice multiplication! Pick a times table (or 0 for a mix of all of them),
  answer 10 questions, and see your score and how fast you were.

  HOW TO RUN: g++ -std=c++17 times-table-quiz.cpp -o quiz   then ./quiz
*/
#include <chrono>
#include <iostream>
#include <limits>
#include <random>

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
        std::cout << "Numbers only, please: ";
    }
}

int main() {
    const int QUESTION_COUNT = 10;

    std::random_device seed;
    std::mt19937 rng(seed());
    std::uniform_int_distribution<int> randomFactor(1, 12);

    std::cout << "=== TIMES TABLE QUIZ ===\n";
    std::cout << "Which times table? (1-12, or 0 for a mix): ";
    int table = 0;
    if (!readNumber(table)) {
        return 0;
    }
    if (table < 0 || table > 12) {
        std::cout << "I'll give you a mix then!\n";
        table = 0;
    }

    int correct = 0;
    int streak = 0;
    int bestStreak = 0;
    auto startTime = std::chrono::steady_clock::now();

    for (int question = 1; question <= QUESTION_COUNT; question++) {
        int a = (table == 0) ? randomFactor(rng) : table;
        int b = randomFactor(rng);

        std::cout << "\nQuestion " << question << ": " << a << " x " << b << " = ";
        int answer = 0;
        if (!readNumber(answer)) {
            std::cout << "\nQuiz stopped.\n";
            return 0;
        }

        if (answer == a * b) {
            correct++;
            streak++;
            if (streak > bestStreak) {
                bestStreak = streak;
            }
            std::cout << "Correct!";
            if (streak >= 3) {
                std::cout << " " << streak << " in a row!";
            }
            std::cout << "\n";
        } else {
            streak = 0;
            std::cout << "Not quite. " << a << " x " << b << " = " << a * b << "\n";
        }
    }

    auto endTime = std::chrono::steady_clock::now();
    auto seconds = std::chrono::duration_cast<std::chrono::seconds>(endTime - startTime).count();

    std::cout << "\n=== RESULTS ===\n";
    std::cout << "Score: " << correct << " / " << QUESTION_COUNT << "\n";
    std::cout << "Best streak: " << bestStreak << "\n";
    std::cout << "Time: " << seconds << " seconds\n";

    if (correct == QUESTION_COUNT) {
        std::cout << "PERFECT SCORE! You're a math legend!\n";
    } else if (correct >= QUESTION_COUNT * 7 / 10) {
        std::cout << "Great job! Keep practicing!\n";
    } else {
        std::cout << "Good try! Practice makes perfect.\n";
    }
    return 0;
}
