-- SQL Server init script for Contoso Outfitters orders.

-- The SQL Server image doesn't run init scripts, so the AppHost passes this file to WithCreationScript instead.
-- Aspire runs it, batch by batch, every time the SQL Server resource starts, so every statement is idempotent.
-- The API's development-only "Reset orders" endpoint drops the tables and runs it again.

-- Create the orders database
IF DB_ID(N'orders') IS NULL
BEGIN
    CREATE DATABASE [orders];
END;
GO

USE [orders];
GO

-- Create the Orders table. CustomerId refers to a customer in the MySQL customers database.
IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Orders
    (
        Id         INT IDENTITY(1, 1) PRIMARY KEY,
        CustomerId INT            NOT NULL,
        PlacedAt   DATETIMEOFFSET NOT NULL CONSTRAINT DF_Orders_PlacedAt DEFAULT SYSDATETIMEOFFSET(),
        Status     VARCHAR(20)    NOT NULL CONSTRAINT DF_Orders_Status DEFAULT 'Placed'
                   CONSTRAINT CK_Orders_Status CHECK (Status IN ('Placed', 'Packed', 'Shipped', 'Delivered')),
        Total      DECIMAL(10, 2) NOT NULL
    );

    CREATE INDEX IX_Orders_CustomerId ON dbo.Orders (CustomerId);
END;
GO

-- Create the OrderLines table. ProductId refers to a product in the PostgreSQL catalog database, and the name and
-- price are copied from the catalog when the order is placed.
IF OBJECT_ID(N'dbo.OrderLines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OrderLines
    (
        OrderId     INT            NOT NULL CONSTRAINT FK_OrderLines_Orders REFERENCES dbo.Orders (Id) ON DELETE CASCADE,
        ProductId   INT            NOT NULL,
        ProductName NVARCHAR(200)  NOT NULL,
        Quantity    INT            NOT NULL CONSTRAINT CK_OrderLines_Quantity CHECK (Quantity > 0),
        UnitPrice   DECIMAL(10, 2) NOT NULL,
        CONSTRAINT PK_OrderLines PRIMARY KEY (OrderId, ProductId)
    );
END;
GO

-- Seed the recent order history, only if the table is empty
IF NOT EXISTS (SELECT 1 FROM dbo.Orders)
BEGIN
    INSERT INTO dbo.Orders (CustomerId, PlacedAt, Status, Total)
    VALUES
        (1, DATEADD(DAY, -21, SYSDATETIMEOFFSET()), 'Delivered', 0),
        (3, DATEADD(DAY, -18, SYSDATETIMEOFFSET()), 'Delivered', 0),
        (2, DATEADD(DAY, -15, SYSDATETIMEOFFSET()), 'Delivered', 0),
        (7, DATEADD(DAY, -11, SYSDATETIMEOFFSET()), 'Delivered', 0),
        (5, DATEADD(DAY, -8, SYSDATETIMEOFFSET()), 'Shipped', 0),
        (8, DATEADD(DAY, -6, SYSDATETIMEOFFSET()), 'Shipped', 0),
        (12, DATEADD(DAY, -4, SYSDATETIMEOFFSET()), 'Shipped', 0),
        (4, DATEADD(DAY, -3, SYSDATETIMEOFFSET()), 'Packed', 0),
        (10, DATEADD(HOUR, -30, SYSDATETIMEOFFSET()), 'Packed', 0),
        (6, DATEADD(HOUR, -5, SYSDATETIMEOFFSET()), 'Placed', 0),
        (9, DATEADD(HOUR, -2, SYSDATETIMEOFFSET()), 'Placed', 0),
        (11, DATEADD(MINUTE, -40, SYSDATETIMEOFFSET()), 'Placed', 0);

    INSERT INTO dbo.OrderLines (OrderId, ProductId, ProductName, Quantity, UnitPrice)
    VALUES
        (1, 1, N'Trailblazer 45L Backpack', 1, 189.00),
        (1, 12, N'Glacier Insulated Bottle 1L', 2, 34.00),
        (2, 3, N'Summit Down Jacket', 1, 279.00),
        (2, 4, N'Alpine Merino Base Layer', 2, 89.00),
        (3, 6, N'Cascade Waterproof Hiking Boots', 1, 214.00),
        (4, 9, N'Nightfall Down Sleeping Bag', 1, 229.00),
        (4, 10, N'Titanium Camp Stove', 1, 64.00),
        (4, 13, N'Lumen 400 Headlamp', 1, 49.00),
        (5, 5, N'Storm Shell Rain Jacket', 1, 159.00),
        (5, 2, N'Daypack 22L', 1, 79.00),
        (6, 12, N'Glacier Insulated Bottle 1L', 3, 34.00),
        (7, 14, N'Wayfinder GPS Watch', 1, 399.00),
        (7, 11, N'Ridgeline Carbon Trekking Poles', 1, 129.00),
        (8, 7, N'Ridgeback Trail Running Shoes', 1, 139.00),
        (9, 1, N'Trailblazer 45L Backpack', 1, 189.00),
        (9, 13, N'Lumen 400 Headlamp', 2, 49.00),
        (10, 4, N'Alpine Merino Base Layer', 1, 89.00),
        (10, 12, N'Glacier Insulated Bottle 1L', 1, 34.00),
        (11, 8, N'Basecamp 2-Person Tent', 1, 349.00),
        (12, 2, N'Daypack 22L', 1, 79.00),
        (12, 10, N'Titanium Camp Stove', 1, 64.00);

    UPDATE o
    SET Total = (SELECT SUM(l.Quantity * l.UnitPrice) FROM dbo.OrderLines l WHERE l.OrderId = o.Id)
    FROM dbo.Orders o;
END;
GO
