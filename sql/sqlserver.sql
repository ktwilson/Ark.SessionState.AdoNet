-- Ark.SessionState.AdoNet - SQL Server / Azure SQL
-- providerName: System.Data.SqlClient or Microsoft.Data.SqlClient
CREATE TABLE dbo.AspNetSessionState (
    SessionId       varchar(88)     NOT NULL,
    ApplicationName varchar(255)    NOT NULL,
    Created         datetime        NOT NULL,
    Expires         datetime        NOT NULL,
    LockDate        datetime        NOT NULL,
    LockId          int             NOT NULL,
    Timeout         int             NOT NULL,
    Locked          int             NOT NULL,
    SessionItems    varbinary(max)  NULL,
    Flags           int             NOT NULL,
    CONSTRAINT PK_AspNetSessionState PRIMARY KEY (SessionId, ApplicationName)
);

CREATE INDEX IX_AspNetSessionState_Expires ON dbo.AspNetSessionState (Expires);
