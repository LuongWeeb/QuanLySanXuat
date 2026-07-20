IF DB_ID(N'WmsMesDb') IS NULL
BEGIN
    CREATE DATABASE [WmsMesDb];
END;
GO

DECLARE @MigrationPassword nvarchar(128) = N'$(MSSQL_MIGRATION_PASSWORD)';
DECLARE @ApplicationPassword nvarchar(128) = N'$(MSSQL_APP_PASSWORD)';

IF SUSER_ID(N'wmsmes_migrator') IS NULL
BEGIN
    EXEC sys.sp_addlogin
        @loginame = N'wmsmes_migrator',
        @passwd = @MigrationPassword,
        @defdb = N'WmsMesDb';
END
ELSE
BEGIN
    EXEC sys.sp_password
        @old = NULL,
        @new = @MigrationPassword,
        @loginame = N'wmsmes_migrator';
END;

IF SUSER_ID(N'wmsmes_app') IS NULL
BEGIN
    EXEC sys.sp_addlogin
        @loginame = N'wmsmes_app',
        @passwd = @ApplicationPassword,
        @defdb = N'WmsMesDb';
END
ELSE
BEGIN
    EXEC sys.sp_password
        @old = NULL,
        @new = @ApplicationPassword,
        @loginame = N'wmsmes_app';
END;
GO

USE [WmsMesDb];
GO

IF DATABASE_PRINCIPAL_ID(N'wmsmes_migrator') IS NULL
BEGIN
    CREATE USER [wmsmes_migrator] FOR LOGIN [wmsmes_migrator];
END;

IF DATABASE_PRINCIPAL_ID(N'wmsmes_app') IS NULL
BEGIN
    CREATE USER [wmsmes_app] FOR LOGIN [wmsmes_app];
END;

IF IS_ROLEMEMBER(N'db_owner', N'wmsmes_migrator') <> 1
BEGIN
    ALTER ROLE [db_owner] ADD MEMBER [wmsmes_migrator];
END;

IF IS_ROLEMEMBER(N'db_owner', N'wmsmes_app') = 1
BEGIN
    ALTER ROLE [db_owner] DROP MEMBER [wmsmes_app];
END;

IF IS_ROLEMEMBER(N'db_datareader', N'wmsmes_app') <> 1
BEGIN
    ALTER ROLE [db_datareader] ADD MEMBER [wmsmes_app];
END;

IF IS_ROLEMEMBER(N'db_datawriter', N'wmsmes_app') <> 1
BEGIN
    ALTER ROLE [db_datawriter] ADD MEMBER [wmsmes_app];
END;
GO
