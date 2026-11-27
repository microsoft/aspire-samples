-- MySQL init script for Contoso Outfitters customer accounts.

-- The mysql image runs every *.sql/*.sh file in /docker-entrypoint-initdb.d the first time the container
-- starts with an empty data directory. It runs them against the database named by MYSQL_DATABASE ("customers").

-- NOTE: MySQL database and table names are case-sensitive on non-Windows platforms!
--       Column names are always case-insensitive.

-- Create the customers table
CREATE TABLE IF NOT EXISTS `customers`
(
    `id` INT NOT NULL AUTO_INCREMENT,
    `first_name` VARCHAR(100) NOT NULL,
    `last_name` VARCHAR(100) NOT NULL,
    `email` VARCHAR(255) NOT NULL,
    `city` VARCHAR(100) NOT NULL,
    `country` CHAR(2) NOT NULL,
    `loyalty_tier` ENUM('Bronze', 'Silver', 'Gold') NOT NULL DEFAULT 'Bronze',
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_customers_email` (`email`)
);

-- Seed the customer accounts (INSERT IGNORE skips rows whose email already exists)
INSERT IGNORE INTO `customers` (`first_name`, `last_name`, `email`, `city`, `country`, `loyalty_tier`)
VALUES
    ('Ava', 'Thompson', 'ava.thompson@example.com', 'Denver', 'US', 'Gold'),
    ('Mateo', 'Alvarez', 'mateo.alvarez@example.com', 'Seattle', 'US', 'Silver'),
    ('Priya', 'Raman', 'priya.raman@example.com', 'Toronto', 'CA', 'Gold'),
    ('Liam', 'O''Connor', 'liam.oconnor@example.com', 'Dublin', 'IE', 'Bronze'),
    ('Sofia', 'Rossi', 'sofia.rossi@example.com', 'Milan', 'IT', 'Silver'),
    ('Kenji', 'Watanabe', 'kenji.watanabe@example.com', 'Sapporo', 'JP', 'Bronze'),
    ('Emma', 'Schulz', 'emma.schulz@example.com', 'Munich', 'DE', 'Gold'),
    ('Noah', 'Williams', 'noah.williams@example.com', 'Portland', 'US', 'Bronze'),
    ('Chloe', 'Martin', 'chloe.martin@example.com', 'Lyon', 'FR', 'Silver'),
    ('Oliver', 'Bennett', 'oliver.bennett@example.com', 'Vancouver', 'CA', 'Silver'),
    ('Isla', 'MacLeod', 'isla.macleod@example.com', 'Edinburgh', 'GB', 'Bronze'),
    ('Lucas', 'Silva', 'lucas.silva@example.com', 'Lisbon', 'PT', 'Gold');
