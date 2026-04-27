# OnlineMatchRepository Phase

Implemented the Online Match Repository and Models. 

**Models**: 
- `OnlineMatch`: Represents a match session with properties like `Id`, `Status`, `CurrentSnapshotId`, `CreatedAt`, `UpdatedAt`.
- `OnlineMatchPlayer`: Represents players participating in an online match (`MatchId`, `PlayerId`, `PlayerName`, `ColorAssignment`).

**Repository**:
- `OnlineMatchRepository`: Provides operations for matches interacting with `online_matches` and `online_match_players` tables.
- Included methods:
  - `CreateMatchAsync`: Inserts a new match returning the generated Id.
  - `ListOpenMatchesAsync`: Returns top 50 matches waiting for players.
  - `JoinMatchAsync`: Assigns a player to an existing match, checking constraints (e.g. max players 2), and sets status to InProgress if full.
  - `UpdateMatchStatusAsync`: Updates the current match status and optionally updates `CurrentSnapshotId`.

**Constraints Handled**:
- Uses `IDbConnectionFactory` with `MySqlConnector`.
- No EF Core usage.
- Used parameterised queries for SQL statements to prevent SQL injection.