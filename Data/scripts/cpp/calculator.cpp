/*
  CALCULATOR  (Console C++)
  Type a math problem like:  12 + 7   or   9 / 2   or   2 ^ 10
  Operators: +  -  *  /  %  ^ (power).  Type q to quit.
  Type "ans" instead of a number to use your last answer!

  HOW TO RUN: g++ -std=c++17 calculator.cpp -o calc   then ./calc
*/
#include <cmath>
#include <iostream>
#include <limits>
#include <sstream>
#include <string>

// Turns text into a number. "ans" means the last answer. Returns false if it's not a number.
bool parseNumber(const std::string& text, double lastAnswer, double& result) {
    if (text == "ans" || text == "ANS") {
        result = lastAnswer;
        return true;
    }
    std::istringstream stream(text);
    stream >> result;
    return !stream.fail() && stream.eof();
}

int main() {
    double lastAnswer = 0.0;

    std::cout << "=== C++ CALCULATOR ===\n";
    std::cout << "Type things like: 5 + 3   (put spaces between!)   q = quit\n";

    std::string line;
    while (true) {
        std::cout << "\n> ";
        if (!std::getline(std::cin, line)) {
            break;  // input ended
        }
        if (line == "q" || line == "Q" || line == "quit") {
            break;
        }
        if (line.empty()) {
            continue;
        }

        std::istringstream parts(line);
        std::string leftText;
        std::string op;
        std::string rightText;
        std::string extra;
        if (!(parts >> leftText >> op >> rightText) || (parts >> extra)) {
            std::cout << "Please type: number operator number   (example: 4 * 6)\n";
            continue;
        }

        double left = 0.0;
        double right = 0.0;
        if (!parseNumber(leftText, lastAnswer, left) || !parseNumber(rightText, lastAnswer, right)) {
            std::cout << "I couldn't read one of those numbers.\n";
            continue;
        }

        double result = 0.0;
        bool ok = true;
        if (op == "+") {
            result = left + right;
        } else if (op == "-") {
            result = left - right;
        } else if (op == "*" || op == "x") {
            result = left * right;
        } else if (op == "/") {
            if (right == 0.0) {
                std::cout << "Oops! You can't divide by zero.\n";
                ok = false;
            } else {
                result = left / right;
            }
        } else if (op == "%") {
            if (right == 0.0) {
                std::cout << "Oops! You can't do % by zero.\n";
                ok = false;
            } else {
                result = std::fmod(left, right);  // the remainder
            }
        } else if (op == "^") {
            result = std::pow(left, right);
        } else {
            std::cout << "I don't know the operator \"" << op << "\". Use + - * / % ^\n";
            ok = false;
        }

        if (ok) {
            std::cout << "= " << result << "\n";
            lastAnswer = result;
        }
    }

    std::cout << "Goodbye!\n";
    return 0;
}
