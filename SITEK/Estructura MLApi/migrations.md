// ============================================
// MIGRACIONES (Package Manager Console)
// ============================================

/*
Add-Migration InitialMLIntegration
Update-Database
*/

// ============================================
// SQL SCRIPT ALTERNATIVO (si no usas EF Migrations)
// ============================================

/*
CREATE TABLE MLTokens (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    UserId INT NOT NULL UNIQUE,
    AccessToken NVARCHAR(MAX) NOT NULL,
    RefreshToken NVARCHAR(MAX) NOT NULL,
    ExpiresAt DATETIME2 NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX IX_MLTokens_UserId ON MLTokens(UserId);

CREATE TABLE MLProducts (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    UserId INT NOT NULL,
    ItemId NVARCHAR(50) NOT NULL UNIQUE,
    FamilyName NVARCHAR(MAX) NOT NULL,
    CategoryId NVARCHAR(20) NOT NULL,
    Price DECIMAL(18,2) NOT NULL,
    AvailableQuantity INT NOT NULL,
    Status NVARCHAR(20) NOT NULL,
    SubStatus NVARCHAR(50) NULL,
    LastSync DATETIME2 NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX IX_MLProducts_ItemId ON MLProducts(ItemId);
CREATE INDEX IX_MLProducts_UserId ON MLProducts(UserId);
*/