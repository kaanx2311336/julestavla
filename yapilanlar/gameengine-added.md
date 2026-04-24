# 2024-05-xx (Today's Date) - Added GameEngine

- Implemented `GameEngine` in `src/TavlaJules.Engine/Engine/GameEngine.cs` to apply valid moves to the board.
- Integrated `MoveValidator` into `GameEngine`.
- Handled board state changes: removing checkers, adding checkers, hitting, and bearing off.
- Added unit tests for `GameEngine` in `src/TavlaJules.Engine.Tests/Engine/GameEngineTests.cs` covering normal moves, hits, invalid moves, and bearing off.
- Verified all tests pass.
