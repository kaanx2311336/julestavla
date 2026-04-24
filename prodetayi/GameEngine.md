Summarizes the `GameEngine` class located at `src/TavlaJules.Engine/Engine/GameEngine.cs`.

*   **Purpose:** The main class responsible for managing the state and rules of the backgammon game.
*   **Key Components:**
    *   Holds the `Board`, `MoveValidator`, and `CurrentTurn`.
    *   Tracks the `RemainingDice` for the current turn to strictly enforce that moves are only made with valid dice, and properties like `IsTurnComplete`.
    *   `RollDice(Random?)` allows generating 2 dice rolls deterministically.
    *   `StartTurn(PlayerColor, int, int)` establishes the turn rules, converting double dice rolls to four distinct dice.
    *   `AdvanceTurn()` switches current players after all dice are consumed.
    *   `ApplyMove(Move)` executes single checker moves securely by inferring the dice roll, ensuring there is a corresponding remaining die, consuming the die, hitting, and bearing off.
    *   `GenerateLegalMoves(PlayerColor)` finds all available correct single moves for the player depending on the unconsumed dice.
    *   `GenerateLegalMoves(PlayerColor, (int die1, int die2))` finds legal single moves for supplied dice without requiring an active turn, useful for UI previews, AI analysis, and online replay validation.
    *   Generated `Move` values now include `DiceUsed`, and hit candidates mark `IsHit` before application.
