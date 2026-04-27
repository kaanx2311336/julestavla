# Online Match Schema

The online match schema introduces the foundational MySQL tables needed to support multiplayer online gameplay.

## Tables

- **`online_matches`**: Represents an individual online match session.
  - `id`: Unique identifier for the match.
  - `status`: Current status of the match (e.g., pending, in-progress, completed).
  - `current_snapshot_id`: Optional reference to the latest game state snapshot.
  - `created_at`: Timestamp of match creation.
  - `updated_at`: Timestamp of the last update.

- **`online_match_players`**: Represents players participating in an online match.
  - `id`: Unique identifier for the player-match link.
  - `match_id`: Reference to the `online_matches` table.
  - `player_id`: Unique identifier for the player.
  - `player_name`: Display name of the player.
  - `color_assignment`: The color assigned to the player (e.g., White, Black).
  - `created_at`: Timestamp when the player joined the match.
  - `updated_at`: Timestamp of the last update.
