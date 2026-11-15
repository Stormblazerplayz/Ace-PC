/*
  TIC-TAC-TOE (2 players)  (Console C++)
  Player X and Player O take turns on the same keyboard.
  Pick a square by typing its number:

     1 | 2 | 3
    ---+---+---
     4 | 5 | 6
    ---+---+---
     7 | 8 | 9

  HOW TO RUN: g++ -std=c++17 tic-tac-toe.cpp -o ttt   then ./ttt
*/
#include <array>
#include <iostream>
#include <limits>
#include <string>

using Board = std::array<char, 9>;  // 9 squares. ' ' = empty, 'X' or 'O' = taken

void drawBoard(const Board& board) {
    std::cout << "\n";
    for (int row = 0; row < 3; row++) {
        std::cout << " ";
        for (int col = 0; col < 3; col++) {
            int index = row * 3 + col;
            // Show the number if the square is empty, so players know what to type
            char shown = board[index] == ' ' ? static_cast<char>('1' + index) : board[index];
            std::cout << shown;
            if (col < 2) {
                std::cout << " | ";
            }
        }
        std::cout << "\n";
        if (row < 2) {
            std::cout << "---+---+---\n";
        }
    }
    std::cout << "\n";
}

// Returns 'X' or 'O' if someone has 3 in a row, otherwise ' '
char findWinner(const Board& board) {
    const int lines[8][3] = {
        {0, 1, 2}, {3, 4, 5}, {6, 7, 8},  // rows
        {0, 3, 6}, {1, 4, 7}, {2, 5, 8},  // columns
        {0, 4, 8}, {2, 4, 6}              // diagonals
    };
    for (const auto& line : lines) {
        char first = board[line[0]];
        if (first != ' ' && first == board[line[1]] && first == board[line[2]]) {
            return first;
        }
    }
    return ' ';
}

bool boardFull(const Board& board) {
    for (char square : board) {
        if (square == ' ') {
            return false;
        }
    }
    return true;
}

int main() {
    int winsX = 0;
    int winsO = 0;
    int ties = 0;

    std::cout << "=== TIC-TAC-TOE ===\n";

    bool playAgain = true;
    while (playAgain) {
        Board board;
        board.fill(' ');
        char player = 'X';

        while (true) {
            drawBoard(board);
            std::cout << "Player " << player << ", pick a square (1-9): ";

            int choice = 0;
            if (!(std::cin >> choice)) {
                if (std::cin.eof()) {
                    std::cout << "\nBye!\n";
                    return 0;
                }
                std::cin.clear();
                std::cin.ignore(std::numeric_limits<std::streamsize>::max(), '\n');
                std::cout << "Type a number from 1 to 9.\n";
                continue;
            }
            if (choice < 1 || choice > 9) {
                std::cout << "That square doesn't exist. Use 1 to 9.\n";
                continue;
            }
            if (board[choice - 1] != ' ') {
                std::cout << "That square is already taken!\n";
                continue;
            }

            board[choice - 1] = player;

            char winner = findWinner(board);
            if (winner != ' ') {
                drawBoard(board);
                std::cout << "PLAYER " << winner << " WINS!\n";
                if (winner == 'X') {
                    winsX++;
                } else {
                    winsO++;
                }
                break;
            }
            if (boardFull(board)) {
                drawBoard(board);
                std::cout << "It's a tie!\n";
                ties++;
                break;
            }

            player = (player == 'X') ? 'O' : 'X';  // switch turns
        }

        std::cout << "Score -> X: " << winsX << "  O: " << winsO << "  Ties: " << ties << "\n";
        std::cout << "Play again? (y/n): ";
        std::string answer;
        if (!(std::cin >> answer)) {
            break;
        }
        playAgain = !answer.empty() && (answer[0] == 'y' || answer[0] == 'Y');
    }

    std::cout << "Thanks for playing!\n";
    return 0;
}
