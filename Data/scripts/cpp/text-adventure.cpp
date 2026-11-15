/*
  TEXT ADVENTURE: THE CRASHED SPACESHIP  (Console C++)
  You wake up in a crashed spaceship on an alien planet. Pick choices by typing
  their number. Find the items you need and escape! There is more than one ending.

  HOW TO RUN: g++ -std=c++17 text-adventure.cpp -o adventure   then ./adventure
*/
#include <iostream>
#include <limits>
#include <set>
#include <string>
#include <vector>

// The places you can be
enum class Room { Cockpit, Hallway, Engine, Outside, Cave, Escaped, GameOver };

std::set<std::string> inventory;  // the items you're carrying (a set never has duplicates)
bool inputEnded = false;

bool has(const std::string& item) {
    return inventory.count(item) > 0;
}

void pickUp(const std::string& item) {
    if (!has(item)) {
        inventory.insert(item);
        std::cout << "  >> You picked up: " << item << "!\n";
    }
}

// Shows choices and returns which one was picked (1, 2, 3 and so on)
int choose(const std::vector<std::string>& options) {
    for (std::size_t i = 0; i < options.size(); i++) {
        std::cout << "  " << (i + 1) << ") " << options[i] << "\n";
    }
    while (true) {
        std::cout << "> ";
        int pick = 0;
        if (std::cin >> pick) {
            if (pick >= 1 && pick <= static_cast<int>(options.size())) {
                return pick;
            }
            std::cout << "Pick a number from 1 to " << options.size() << ".\n";
            continue;
        }
        if (std::cin.eof()) {
            inputEnded = true;
            return 0;
        }
        std::cin.clear();
        std::cin.ignore(std::numeric_limits<std::streamsize>::max(), '\n');
        std::cout << "Type the number of your choice.\n";
    }
}

void showInventory() {
    std::cout << "  [Bag: ";
    if (inventory.empty()) {
        std::cout << "empty";
    }
    bool first = true;
    for (const std::string& item : inventory) {
        std::cout << (first ? "" : ", ") << item;
        first = false;
    }
    std::cout << "]\n";
}

Room cockpit() {
    std::cout << "\n== COCKPIT ==\nSparks fly from the control panel. The windshield is cracked.\n";
    showInventory();
    int pick = choose({"Search the control panel", "Go to the hallway", "Try the radio"});
    if (pick == 1) {
        pickUp("flashlight");
        return Room::Cockpit;
    }
    if (pick == 2) {
        return Room::Hallway;
    }
    if (pick == 3) {
        std::cout << "The radio crackles: \"Need a power cell to launch!\" Then it dies.\n";
        return Room::Cockpit;
    }
    return Room::GameOver;
}

Room hallway() {
    std::cout << "\n== HALLWAY ==\nThe hallway is tilted. A door leads outside and another to the engine room.\n";
    showInventory();
    int pick = choose({"Go to the engine room", "Open the outside door", "Go back to the cockpit"});
    if (pick == 1) {
        return Room::Engine;
    }
    if (pick == 2) {
        return Room::Outside;
    }
    if (pick == 3) {
        return Room::Cockpit;
    }
    return Room::GameOver;
}

Room engine() {
    std::cout << "\n== ENGINE ROOM ==\n";
    if (has("power cell")) {
        std::cout << "You slot the glowing power cell into the engine. It roars to life!\n";
        int pick = choose({"LAUNCH!", "Wait, go back to the hallway"});
        if (pick == 1) {
            return Room::Escaped;
        }
        return pick == 2 ? Room::Hallway : Room::GameOver;
    }
    std::cout << "The engine is dead. There's an empty slot shaped like a power cell.\n";
    showInventory();
    int pick = choose({"Go back to the hallway"});
    return pick == 1 ? Room::Hallway : Room::GameOver;
}

Room outside() {
    std::cout << "\n== ALIEN PLANET ==\nPurple grass. Two moons. A dark cave is nearby, and something is growling.\n";
    showInventory();
    int pick = choose({"Enter the dark cave", "Go back inside the ship", "Walk toward the growling"});
    if (pick == 1) {
        return Room::Cave;
    }
    if (pick == 2) {
        return Room::Hallway;
    }
    if (pick == 3) {
        std::cout << "A giant space slug! It looks hungry!\n";
        int run = choose({"Run back to the ship", "Try to pet it"});
        if (run == 1) {
            return Room::Hallway;
        }
        if (run == 2) {
            std::cout << "The slug was VERY hungry. Gulp!\n";
        }
        return Room::GameOver;
    }
    return Room::GameOver;
}

Room cave() {
    std::cout << "\n== CAVE ==\n";
    if (!has("flashlight")) {
        std::cout << "It's pitch black. You can't see anything. Maybe you need a light?\n";
        int pick = choose({"Go back outside", "Feel your way forward in the dark"});
        if (pick == 2) {
            std::cout << "You trip and fall into a deep hole. Ouch!\n";
            return Room::GameOver;
        }
        return pick == 1 ? Room::Outside : Room::GameOver;
    }
    std::cout << "Your flashlight shows glowing crystals, and a power cell from your ship!\n";
    pickUp("power cell");
    int pick = choose({"Head back outside"});
    return pick == 1 ? Room::Outside : Room::GameOver;
}

int main() {
    std::cout << "=== THE CRASHED SPACESHIP ===\n";
    std::cout << "BOOM! You wake up on the floor of your crashed ship. You need to get home!\n";

    Room room = Room::Cockpit;
    int moves = 0;
    while (room != Room::Escaped && room != Room::GameOver) {
        moves++;
        switch (room) {
            case Room::Cockpit: room = cockpit(); break;
            case Room::Hallway: room = hallway(); break;
            case Room::Engine: room = engine(); break;
            case Room::Outside: room = outside(); break;
            case Room::Cave: room = cave(); break;
            case Room::Escaped:
            case Room::GameOver: break;
        }
        if (inputEnded) {
            std::cout << "\nSee you next time, space explorer!\n";
            return 0;
        }
    }

    if (room == Room::Escaped) {
        std::cout << "\nThe ship blasts off into space. YOU ESCAPED in " << moves << " moves! THE END.\n";
    } else {
        std::cout << "\nGAME OVER. Run the program again to try a different path!\n";
    }
    return 0;
}
