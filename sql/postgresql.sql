-- Ark.SessionState.AdoNet - PostgreSQL
-- providerName: Npgsql
CREATE TABLE AspNetSessionState (
    SessionId       varchar(88)     NOT NULL,
    ApplicationName varchar(255)    NOT NULL,
    Created         timestamp       NOT NULL,
    Expires         timestamp       NOT NULL,
    LockDate        timestamp       NOT NULL,
    LockId          integer         NOT NULL,
    Timeout         integer         NOT NULL,
    Locked          integer         NOT NULL,
    SessionItems    bytea           NULL,
    Flags           integer         NOT NULL,
    CONSTRAINT PK_AspNetSessionState PRIMARY KEY (SessionId, ApplicationName)
);

CREATE INDEX IX_AspNetSessionState_Expires ON AspNetSessionState (Expires);
