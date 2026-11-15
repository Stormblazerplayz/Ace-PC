/*
  VIRTUAL PET (simple class example)  (Console C++)
  Learn about classes! A class is a blueprint. Our Pet class has:
   - private data (hunger, happiness, energy) that only the pet can change directly
   - public methods (feed, play, sleep) that you can call
  Take care of your pet for 10 days!

  HOW TO RUN: g++ -std=c++17 pet-class.cpp -o pet   then ./pet
*/
#include <algorithm>
#include <iostream>
#include <string>

class Pet {
public:
    // The constructor runs when you make a new Pet
    Pet(const std::string& name, const std::string& species)
        : name_(name), species_(species) {}

    void feed() {
        if (hunger_ == 0) {
            std::cout << name_ << " is too full to eat!\n";
            return;
        }
        hunger_ = clamp(hunger_ - 40);
        energy_ = clamp(energy_ + 5);
        std::cout << name_ << " munches happily. Yum!\n";
    }

    void play() {
        if (energy_ < 15) {
            std::cout << name_ << " is too tired to play. Try sleeping.\n";
            return;
        }
        happiness_ = clamp(happiness_ + 25);
        energy_ = clamp(energy_ - 20);
        hunger_ = clamp(hunger_ + 15);
        std::cout << name_ << " runs around and has a blast!\n";
    }

    void sleep() {
        energy_ = 100;
        hunger_ = clamp(hunger_ + 5);
        std::cout << name_ << " curls up and sleeps. Zzz!\n";
    }

    // Time passes: the pet gets hungrier, a bit sadder and more tired
    void passTime() {
        hunger_ = clamp(hunger_ + 8);
        happiness_ = clamp(happiness_ - 10);
        energy_ = clamp(energy_ - 5);
        if (hunger_ >= 80) {
            happiness_ = clamp(happiness_ - 10);  // being hungry makes pets grumpy
        }
    }

    // "const" means this method only LOOKS at the pet, it doesn't change it
    void showStatus() const {
        std::cout << "\n" << name_ << " the " << species_ << " " << face() << "\n";
        std::cout << "  Hunger:    " << bar(hunger_) << " " << hunger_ << "\n";
        std::cout << "  Happiness: " << bar(happiness_) << " " << happiness_ << "\n";
        std::cout << "  Energy:    " << bar(energy_) << " " << energy_ << "\n";
    }

    bool ranAway() const {
        return happiness_ == 0 || hunger_ == 100;
    }

    const std::string& name() const { return name_; }

private:
    std::string name_;
    std::string species_;
    int hunger_ = 30;      // 0 = full, 100 = starving
    int happiness_ = 70;   // 0 = sad, 100 = super happy
    int energy_ = 80;      // 0 = exhausted, 100 = full of energy

    static int clamp(int value) {
        return std::max(0, std::min(100, value));  // keep it between 0 and 100
    }

    static std::string bar(int value) {
        int filled = value / 10;
        return "[" + std::string(filled, '#') + std::string(10 - filled, '-') + "]";
    }

    std::string face() const {
        if (happiness_ >= 70) return "(^_^)";
        if (happiness_ >= 40) return "(-_-)";
        return "(T_T)";
    }
};

int main() {
    std::cout << "=== VIRTUAL PET ===\n";
    std::cout << "Name your pet: ";
    std::string name;
    if (!std::getline(std::cin, name) || name.empty()) {
        name = "Pixel";
    }
    std::cout << "What kind of animal is it? ";
    std::string species;
    if (!std::getline(std::cin, species) || species.empty()) {
        species = "dragon";
    }

    Pet pet(name, species);  // make an object from the Pet class
    const int DAYS = 10;

    for (int day = 1; day <= DAYS; day++) {
        std::cout << "\n===== DAY " << day << " of " << DAYS << " =====";
        pet.showStatus();
        std::cout << "1) Feed  2) Play  3) Sleep  4) Do nothing\n> ";

        std::string choice;
        if (!std::getline(std::cin, choice)) {
            std::cout << "\nBye! " << pet.name() << " will miss you.\n";
            return 0;
        }

        if (choice == "1") {
            pet.feed();
        } else if (choice == "2") {
            pet.play();
        } else if (choice == "3") {
            pet.sleep();
        } else {
            std::cout << "You relax. " << pet.name() << " looks at you.\n";
        }

        pet.passTime();
        if (pet.ranAway()) {
            pet.showStatus();
            std::cout << "\nOh no! " << pet.name() << " ran away to find a better owner. Try again!\n";
            return 0;
        }
    }

    pet.showStatus();
    std::cout << "\nYou took care of " << pet.name() << " for " << DAYS << " days. You're an awesome owner!\n";
    return 0;
}
