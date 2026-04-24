Summarizes the `GameEngine` class located at `src/TavlaJules.Engine/Engine/GameEngine.cs`.

*   **Purpose:** The main class responsible for managing the state and rules of the backgammon game.
*   **Key Components:**
    *   Holds the `Board`, `MoveValidator` and `CurrentTurn`.
    *   `ApplyMove(Move move, PlayerColor player, int diceRoll)` validates and executes a single move, updating board points, handling hits, and bearing off logic.
    *   `SetTurn` to manually set the current turn.
