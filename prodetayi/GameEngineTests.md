Summarizes the `GameEngineTests` class located at `src/TavlaJules.Engine.Tests/Engine/GameEngineTests.cs`.

*   **Purpose:** Unit tests for `GameEngine`.
*   **Key Test Cases:** 
    * Valid normal move updating board state, invalid move returning false without changing state.
    * Valid bearing off move updating borne-off counts.
    * Valid hit move sending opponent checker to the bar.
    * Deterministic dice rolling tests `RollDice(Random)`.
    * Turn setup tests: `StartTurn` converting doubles to 4 dice, and verifying 1-6 range.
    * Dice consumption tests: Applying move securely consuming correct die and rejecting invalid ones.
    * Turn completion and player switching logic `AdvanceTurn` validations.
    * `GenerateLegalMoves` yielding properly constrained board moves per existing dice.
    * Explicit dice `GenerateLegalMoves(PlayerColor, (die1, die2))` tests for dice metadata, blocked points, bar-entry priority, and bearing-off with larger dice.
