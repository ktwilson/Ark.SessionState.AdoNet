-- Ark.SessionState.AdoNet - MySQL / MariaDB
-- providerName: MySql.Data.MySqlClient or MySqlConnector
CREATE TABLE AspNetSessionState (
    SessionId       VARCHAR(88)     NOT NULL,
    ApplicationName VARCHAR(255)    NOT NULL,
    Created         DATETIME(3)     NOT NULL,
    Expires         DATETIME(3)     NOT NULL,
    LockDate        DATETIME(3)     NOT NULL,
    LockId          INT             NOT NULL,
    Timeout         INT             NOT NULL,
    Locked          INT             NOT NULL,
    SessionItems    LONGBLOB        NULL,
    Flags           INT             NOT NULL,
    CONSTRAINT PK_AspNetSessionState PRIMARY KEY (SessionId, ApplicationName)
) ENGINE=InnoDB;

CREATE INDEX IX_AspNetSessionState_Expires ON AspNetSessionState (Expires);
