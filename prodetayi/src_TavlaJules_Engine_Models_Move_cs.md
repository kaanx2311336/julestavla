# src/TavlaJules.Engine/Models/Move.cs

This file defines the `Move` class for the Tavla engine.
It represents a single move in the game.

## Properties
- `SourcePoint` (int): The starting point of the move.
- `DestinationPoint` (int): The ending point of the move.
- `CheckerCount` (int): The number of checkers being moved.
- `IsHit` (bool): A flag indicating if a hit occurred during this move. Defaults to false.
- `DiceUsed` (int): Optional metadata showing which die produced/generated the move. Defaults to 0 for manually constructed legacy moves.
