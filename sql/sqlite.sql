-- Ark.SessionState.AdoNet - SQLite
-- providerName: System.Data.SQLite or Microsoft.Data.Sqlite
CREATE TABLE AspNetSessionState (
    SessionId       TEXT     NOT NULL,
    ApplicationName TEXT     NOT NULL,
    Created         TEXT     NOT NULL,
    Expires         TEXT     NOT NULL,
    LockDate        TEXT     NOT NULL,
    LockId          INTEGER  NOT NULL,
    Timeout         INTEGER  NOT NULL,
    Locked          INTEGER  NOT NULL,
    SessionItems    BLOB     NULL,
    Flags           INTEGER  NOT NULL,
    CONSTRAINT PK_AspNetSessionState PRIMARY KEY (SessionId, ApplicationName)
);

CREATE INDEX IX_AspNetSessionState_Expires ON AspNetSessionState (Expires);
