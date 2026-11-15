/*
  ROCK PAPER SCISSORS  (Console C++)
  Play against the computer. First to 3 wins takes the match!
  Type r, p or s (or q to quit).

  HOW TO RUN: g++ -std=c++17 rock-paper-scissors.cpp -o rps   then ./rps
*/
#include <iostream>
#include <random>
#include <string>

// We use numbers for the moves: 0 = rock, 1 = paper, 2 = scissors
const std::string MOVE_NAMES[3] = {"Rock", "Paper", "Scissors"};

// Turns a letter into a move number. Returns -1 for "quit", -2 for "not valid".
int letterToMove(char letter) {
    switch (letter) {
        case 'r': case 'R': return 0;
        case 'p': case 'P': return 1;
        case 's': case 'S': return 2;
        case 'q': case 'Q': return -1;
        default: return -2;
    }
}

// Returns 1 if the player wins, -1 if the computer wins, 0 for a tie.
int whoWins(int player, int computer) {
    if (player == computer) {
        return 0;
    }
    // Each move beats the one "before" it: paper(1) beats rock(0), scissors(2) beats paper(1),
    // rock(0) beats scissors(2). (player - computer + 3) % 3 == 1 means the player wins.
    return ((player - computer + 3) % 3 == 1) ? 1 : -1;
}

int main() {
    std::random_device seed;
    std::mt19937 rng(seed());
    std::uniform_int_distribution<int> randomMove(0, 2);

    const int WINS_NEEDED = 3;
    int playerScore = 0;
    int computerScore = 0;

    std::cout << "=== ROCK PAPER SCISSORS ===\n";
    std::cout << "First to " << WINS_NEEDED << " wins!\n";

    while (playerScore < WINS_NEEDED && computerScore < WINS_NEEDED) {
        std::cout << "\n(r)ock, (p)aper, (s)cissors or (q)uit: ";
        std::string input;
        if (!(std::cin >> input)) {
            std::cout << "\nBye!\n";
            return 0;
        }

        int player = letterToMove(input[0]);
        if (player == -1) {
            std::cout << "You quit. Final score: You " << playerScore << " - " << computerScore << " Computer\n";
            return 0;
        }
        if (player == -2) {
            std::cout << "Type r, p, s or q!\n";
            continue;
        }

        int computer = randomMove(rng);
        std::cout << "You: " << MOVE_NAMES[player] << "   Computer: " << MOVE_NAMES[computer] << "\n";

        int result = whoWins(player, computer);
        if (result == 1) {
            playerScore++;
            std::cout << MOVE_NAMES[player] << " beats " << MOVE_NAMES[computer] << ". You win this round!\n";
        } else if (result == -1) {
            computerScore++;
            std::cout << MOVE_NAMES[computer] << " beats " << MOVE_NAMES[player] << ". Computer wins this round.\n";
        } else {
            std::cout << "It's a tie!\n";
        }
        std::cout << "Score: You " << playerScore << " - " << computerScore << " Computer\n";
    }

    if (playerScore > computerScore) {
        std::cout << "\nYOU WON THE MATCH! GG!\n";
    } else {
        std::cout << "\nThe computer won the match. Rematch?\n";
    }
    return 0;
}
