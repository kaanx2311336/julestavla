CREATE TABLE IF NOT EXISTS games (
    id VARCHAR(36) PRIMARY KEY,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    status VARCHAR(50) NOT NULL
);

CREATE TABLE IF NOT EXISTS game_state_snapshots (
    id VARCHAR(36),
    board_layout JSON NOT NULL,
    white_checkers_on_bar INT NOT NULL,
    black_checkers_on_bar INT NOT NULL,
    white_checkers_borne_off INT NOT NULL,
    black_checkers_borne_off INT NOT NULL,
    current_turn VARCHAR(20) NOT NULL,
    remaining_dice JSON NOT NULL,
    turn_number INT NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (id) REFERENCES games(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS move_sequences (
    id VARCHAR(36) PRIMARY KEY,
    game_id VARCHAR(36) NOT NULL,
    moves JSON NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (game_id) REFERENCES games(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS dice_rolls (
    id VARCHAR(36) PRIMARY KEY,
    game_id VARCHAR(36) NOT NULL,
    die1 INT NOT NULL,
    die2 INT NOT NULL,
    player VARCHAR(20) NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (game_id) REFERENCES games(id) ON DELETE CASCADE
);
