-- PostgreSQL init script for the Contoso Outfitters product catalog.

-- The postgres image runs every *.sql/*.sh file in /docker-entrypoint-initdb.d the first time the container
-- starts with an empty data directory. It runs them against the database named by POSTGRES_DB ("catalog").

-- Create the products table
CREATE TABLE IF NOT EXISTS products
(
    id             SERIAL PRIMARY KEY,
    sku            text UNIQUE NOT NULL,
    name           text NOT NULL,
    category       text NOT NULL,
    description    text NOT NULL,
    price          numeric(10, 2) NOT NULL CHECK (price > 0),
    units_in_stock integer NOT NULL CHECK (units_in_stock >= 0)
);

-- Seed the catalog
INSERT INTO products (sku, name, category, description, price, units_in_stock)
VALUES
    ('PK-TRL-45', 'Trailblazer 45L Backpack', 'Packs', 'A ventilated 45-liter trekking pack with an adjustable harness and a stowable rain cover.', 189.00, 42),
    ('PK-DAY-22', 'Daypack 22L', 'Packs', 'A lightweight daypack with a hydration sleeve and hip-belt pockets.', 79.00, 65),
    ('AP-SMT-DJ', 'Summit Down Jacket', 'Apparel', 'An 800-fill down jacket that packs into its own pocket.', 279.00, 18),
    ('AP-ALP-BL', 'Alpine Merino Base Layer', 'Apparel', 'An odor-resistant merino wool base layer for cold mornings on the trail.', 89.00, 120),
    ('AP-STM-RJ', 'Storm Shell Rain Jacket', 'Apparel', 'A three-layer waterproof, breathable shell with pit zips.', 159.00, 35),
    ('FW-CSC-HB', 'Cascade Waterproof Hiking Boots', 'Footwear', 'Waterproof leather hiking boots with a grippy lugged outsole.', 214.00, 27),
    ('FW-RDB-TR', 'Ridgeback Trail Running Shoes', 'Footwear', 'Cushioned trail runners with a rock plate and a quick-pull lacing system.', 139.00, 3),
    ('CP-BSC-2P', 'Basecamp 2-Person Tent', 'Camping', 'A freestanding three-season tent with two doors and two vestibules.', 349.00, 0),
    ('CP-NTF-SB', 'Nightfall Down Sleeping Bag', 'Camping', 'A mummy-shaped down sleeping bag rated to 15 F (-9 C).', 229.00, 12),
    ('CP-TTN-ST', 'Titanium Camp Stove', 'Camping', 'An ultralight canister stove that boils a liter of water in about four minutes.', 64.00, 58),
    ('AC-RDG-TP', 'Ridgeline Carbon Trekking Poles', 'Accessories', 'Collapsible carbon-fiber trekking poles with cork grips.', 129.00, 2),
    ('AC-GLC-1L', 'Glacier Insulated Bottle 1L', 'Accessories', 'A double-wall stainless steel bottle that keeps drinks cold for 24 hours.', 34.00, 200),
    ('AC-LMN-HL', 'Lumen 400 Headlamp', 'Accessories', 'A rechargeable 400-lumen headlamp with a red night-vision mode.', 49.00, 88),
    ('AC-WAY-GW', 'Wayfinder GPS Watch', 'Accessories', 'A multi-band GPS watch with topographic maps and a 14-day battery.', 399.00, 9)
ON CONFLICT (sku) DO NOTHING;
