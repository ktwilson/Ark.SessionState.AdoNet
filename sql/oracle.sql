-- Ark.SessionState.AdoNet - Oracle
-- providerName: Oracle.ManagedDataAccess.Client (ODP.NET managed) or Oracle.DataAccess.Client
CREATE TABLE AspNetSessionState (
    SessionId       VARCHAR2(88)    NOT NULL,
    ApplicationName VARCHAR2(255)   NOT NULL,
    Created         TIMESTAMP       NOT NULL,
    Expires         TIMESTAMP       NOT NULL,
    LockDate        TIMESTAMP       NOT NULL,
    LockId          NUMBER(10)      NOT NULL,
    Timeout         NUMBER(10)      NOT NULL,
    Locked          NUMBER(1)       NOT NULL,
    SessionItems    BLOB            NULL,
    Flags           NUMBER(10)      NOT NULL,
    CONSTRAINT PK_AspNetSessionState PRIMARY KEY (SessionId, ApplicationName)
);

CREATE INDEX IX_AspNetSessionState_Expires ON AspNetSessionState (Expires);
