/*
  TO-DO LIST  (Console C++)
  A simple to-do list that uses std::vector (a list that can grow and shrink).
  Add tasks, mark them done, remove them, and see your list.

  HOW TO RUN: g++ -std=c++17 todo-list.cpp -o todo   then ./todo
*/
#include <iostream>
#include <limits>
#include <string>
#include <vector>

// A struct groups related things together: each task has text and a done/not-done flag
struct Task {
    std::string text;
    bool done = false;
};

void showTasks(const std::vector<Task>& tasks) {
    if (tasks.empty()) {
        std::cout << "Your list is empty! Add something.\n";
        return;
    }
    int doneCount = 0;
    for (std::size_t i = 0; i < tasks.size(); i++) {
        std::cout << "  " << (i + 1) << ". [" << (tasks[i].done ? "X" : " ") << "] " << tasks[i].text << "\n";
        if (tasks[i].done) {
            doneCount++;
        }
    }
    std::cout << "  (" << doneCount << " of " << tasks.size() << " done)\n";
}

// Asks for a task number and checks it's real. Returns -1 if it's not valid.
int askTaskNumber(const std::vector<Task>& tasks) {
    std::cout << "Which number? ";
    int number = 0;
    if (!(std::cin >> number)) {
        if (!std::cin.eof()) {
            std::cin.clear();
        }
        std::cin.ignore(std::numeric_limits<std::streamsize>::max(), '\n');
        return -1;
    }
    std::cin.ignore(std::numeric_limits<std::streamsize>::max(), '\n');
    if (number < 1 || number > static_cast<int>(tasks.size())) {
        return -1;
    }
    return number - 1;  // vectors start counting at 0
}

int main() {
    std::vector<Task> tasks;

    std::cout << "=== MY TO-DO LIST ===\n";

    while (true) {
        std::cout << "\n1) Show list  2) Add  3) Mark done  4) Remove  5) Clear finished  6) Quit\n";
        std::cout << "Choose: ";
        std::string choice;
        if (!std::getline(std::cin, choice)) {
            break;  // input ended
        }

        if (choice == "1") {
            showTasks(tasks);
        } else if (choice == "2") {
            std::cout << "What do you need to do? ";
            std::string text;
            if (!std::getline(std::cin, text)) {
                break;
            }
            if (text.empty()) {
                std::cout << "Nothing added.\n";
            } else {
                tasks.push_back({text, false});  // add to the end of the vector
                std::cout << "Added: " << text << "\n";
            }
        } else if (choice == "3") {
            showTasks(tasks);
            int index = askTaskNumber(tasks);
            if (index < 0) {
                std::cout << "That's not a task number.\n";
            } else {
                tasks[index].done = true;
                std::cout << "Nice work! Finished: " << tasks[index].text << "\n";
            }
        } else if (choice == "4") {
            showTasks(tasks);
            int index = askTaskNumber(tasks);
            if (index < 0) {
                std::cout << "That's not a task number.\n";
            } else {
                std::cout << "Removed: " << tasks[index].text << "\n";
                tasks.erase(tasks.begin() + index);
            }
        } else if (choice == "5") {
            std::vector<Task> stillToDo;
            for (const Task& task : tasks) {
                if (!task.done) {
                    stillToDo.push_back(task);
                }
            }
            std::cout << "Cleared " << (tasks.size() - stillToDo.size()) << " finished task(s).\n";
            tasks = stillToDo;
        } else if (choice == "6" || choice == "q") {
            break;
        } else {
            std::cout << "Pick a number from 1 to 6.\n";
        }
    }

    std::cout << "Bye! Go get things done!\n";
    return 0;
}
