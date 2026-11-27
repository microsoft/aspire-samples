"""Dev-only demo data and traffic for the dashboard's "Seed sample customers" and "Simulate traffic" commands."""

import logging
import random
from datetime import datetime, timedelta, timezone

from customers import (
    CustomerNotFound,
    DuplicateEmail,
    create_customer,
    get_customer,
    list_customers,
    update_customer,
)
from database import connection
from models import CustomerCreate, CustomerStatus, CustomerUpdate, DemoResult, Segment
from telemetry import customer_metrics, tracer

logger = logging.getLogger("contoso.customers")

E, M, S = Segment.enterprise, Segment.mid_market, Segment.smb
LEAD, ACTIVE, CHURNED = CustomerStatus.lead, CustomerStatus.active, CustomerStatus.churned

# (name, email, company, segment, status, country, lifetime value in USD, created days ago)
SAMPLE_CUSTOMERS = [
    ("Avery Johnson", "avery.johnson@northwindtraders.com", "Northwind Traders", E, ACTIVE, "US", 412500, 640),
    ("Mei Tanaka", "mei.tanaka@adventure-works.com", "Adventure Works Cycles", E, ACTIVE, "JP", 286000, 520),
    ("Lukas Becker", "lukas.becker@fabrikam.com", "Fabrikam, Inc.", E, ACTIVE, "DE", 198750, 455),
    ("Sofia Alvarez", "sofia.alvarez@tailspintoys.com", "Tailspin Toys", M, ACTIVE, "MX", 64200, 380),
    ("Oliver Bennett", "oliver.bennett@woodgrovebank.com", "Woodgrove Bank", E, ACTIVE, "GB", 735000, 700),
    ("Priya Sharma", "priya.sharma@litwareinc.com", "Litware, Inc.", M, ACTIVE, "IN", 51800, 300),
    ("Ethan Clarke", "ethan.clarke@proseware.com", "Proseware, Inc.", S, ACTIVE, "CA", 8900, 210),
    ("Hannah Fischer", "hannah.fischer@alpineskihouse.com", "Alpine Ski House", S, ACTIVE, "AT", 12400, 260),
    ("Lucas Moreau", "lucas.moreau@cohowinery.com", "Coho Winery", S, CHURNED, "FR", 6300, 590),
    ("Grace Kim", "grace.kim@fourthcoffee.com", "Fourth Coffee", S, ACTIVE, "US", 15750, 150),
    ("Noah Williams", "noah.williams@wideworldimporters.com", "Wide World Importers", M, ACTIVE, "AU", 92300, 480),
    ("Isabella Rossi", "isabella.rossi@lucernepublishing.com", "Lucerne Publishing", M, CHURNED, "IT", 38600, 610),
    ("Daniel Okafor", "daniel.okafor@treyresearch.net", "Trey Research", M, LEAD, "NG", 0, 12),
    ("Emma Lindqvist", "emma.lindqvist@blueyonderairlines.com", "Blue Yonder Airlines", E, LEAD, "SE", 0, 9),
    ("Mateo Silva", "mateo.silva@margiestravel.com", "Margie's Travel", S, LEAD, "BR", 0, 6),
    ("Chloe Dubois", "chloe.dubois@relecloud.com", "Relecloud", M, ACTIVE, "FR", 77400, 330),
    ("James O'Connor", "james.oconnor@humongousinsurance.com", "Humongous Insurance", E, ACTIVE, "IE", 521000, 560),
    ("Aisha Rahman", "aisha.rahman@lamnahealthcare.com", "Lamna Healthcare", E, LEAD, "SG", 0, 4),
    ("Benjamin Ross", "benjamin.ross@contoso.com", "Contoso Pharmaceuticals", E, ACTIVE, "US", 348900, 690),
    ("Yuki Sato", "yuki.sato@wingtiptoys.com", "Wingtip Toys", S, CHURNED, "JP", 4100, 420),
    ("Lena Novak", "lena.novak@vanarsdel.com", "VanArsdel, Ltd.", M, ACTIVE, "NL", 58900, 240),
    ("Carlos Mendoza", "carlos.mendoza@cpandl.com", "City Power & Light", E, ACTIVE, "ES", 264300, 500),
    ("Zoe Hughes", "zoe.hughes@southridgevideo.com", "Southridge Video", S, LEAD, "US", 0, 2),
    ("Ravi Patel", "ravi.patel@firstupconsultants.com", "First Up Consultants", S, ACTIVE, "CA", 21600, 120),
]

# Inbound leads for "Simulate traffic": a random contact at one of these companies.
FIRST_NAMES = ["Nora", "Samuel", "Julia", "Marcus", "Elena", "Kenji", "Fatima", "Tom", "Ana", "Liam", "Ingrid",
               "Diego", "Amara", "Felix", "Hana", "Omar", "Clara", "Viktor", "Leila", "Jonas"]
LAST_NAMES = ["Haddad", "Adeyemi", "Kowalski", "Chen", "Petrova", "Watanabe", "Zahra", "Gallagher", "Costa",
              "Murphy", "Larsen", "Ramirez", "Nwosu", "Schmidt", "Kobayashi", "Farouk", "Weber", "Ivanov", "Hosseini",
              "Berg"]
PROSPECT_COMPANIES = [
    ("Graphic Design Institute", "graphicdesigninstitute.com", S, "AE"),
    ("Consolidated Messenger", "consolidatedmessenger.com", M, "ZA"),
    ("Bellows College", "bellowscollege.com", M, "PL"),
    ("School of Fine Art", "schooloffineart.com", S, "US"),
    ("Adatum Corporation", "adatum.com", E, "DE"),
    ("Nod Publishers", "nodpublishers.com", S, "JP"),
    ("Best For You Organics", "bestforyouorganics.com", S, "MA"),
    ("Contoso Bank", "contosobank.com", E, "GB"),
    ("Fabrikam Residences", "fabrikamresidences.com", M, "PT"),
    ("Tailspin Logistics", "tailspinlogistics.com", M, "US"),
]

# First-deal sizes in USD by segment; upsells are a fraction of these.
DEAL_SIZES = {E: (60_000, 420_000), M: (12_000, 95_000), S: (1_500, 18_000)}

# How account managers browse the book of business: (status, segment, country, search, offset).
BROWSE_QUERIES = [
    (ACTIVE, E, None, None, 0),
    (None, None, "US", None, 0),
    (LEAD, None, None, None, 0),
    (None, None, None, "contoso", 0),
    (None, None, None, "toys", 0),
    (None, M, None, None, 0),
    (CHURNED, None, None, None, 0),
    (None, None, None, None, 10),
]


async def seed_sample_customers() -> DemoResult:
    """Replace every customer with a realistic book of business."""
    rng = random.Random()
    now = datetime.now(timezone.utc).replace(microsecond=0)
    rows = []
    for name, email, company, segment, status, country, value, days_ago in SAMPLE_CUSTOMERS:
        created = now - timedelta(days=days_ago, hours=rng.randint(0, 20), minutes=rng.randint(0, 59))
        updated = created + timedelta(days=rng.randint(0, min(days_ago, 90)))
        rows.append((name, email, company, segment.value, status.value, country, value, created, min(updated, now)))

    with tracer.start_as_current_span("customers.seed") as span:
        async with connection() as conn, conn.transaction():
            cur = await conn.execute("SELECT status, count(*) AS customers FROM customers GROUP BY status")
            replaced = await cur.fetchall()
            await conn.execute("TRUNCATE customers RESTART IDENTITY")
            async with conn.cursor() as insert:
                await insert.executemany(
                    "INSERT INTO customers (name, email, company, segment, status, country, lifetime_value, "
                    "created_at, updated_at) VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)",
                    rows,
                )

        for row in replaced:
            customer_metrics.count.add(-row["customers"], {"customer.status": row["status"]})
        for _, _, _, segment, status, _, value, _ in SAMPLE_CUSTOMERS:
            customer_metrics.created.add(1, {"customer.segment": segment.value})
            customer_metrics.count.add(1, {"customer.status": status.value})
            if value:
                customer_metrics.revenue.record(value, {"customer.segment": segment.value})

        replaced_total = sum(row["customers"] for row in replaced)
        span.set_attributes({"customers.seed.count": len(rows), "customers.seed.replaced": replaced_total})
        logger.info("Seeded %d sample customers (replaced %d)", len(rows), replaced_total,
                    extra={"customers.seed.count": len(rows), "customers.seed.replaced": replaced_total})
        return DemoResult(seeded=len(rows), total=len(rows))


def _prospect(rng: random.Random) -> CustomerCreate:
    first, last = rng.choice(FIRST_NAMES), rng.choice(LAST_NAMES)
    company, domain, segment, country = rng.choice(PROSPECT_COMPANIES)
    return CustomerCreate(name=f"{first} {last}", email=f"{first}.{last}@{domain}".lower(), company=company,
                          segment=segment, country=country)


def _deal(rng: random.Random, segment: Segment, share: float = 1.0) -> float:
    low, high = DEAL_SIZES[segment]
    return round(rng.uniform(low, high) * share, -2)


async def simulate_traffic(rounds: int) -> DemoResult:
    """A burst of CRM activity: searches, inbound leads, conversions, upsells, churn and win-backs.

    Each call also re-submits an existing lead (409 duplicate email) and opens a stale link (404).
    """
    result = DemoResult(total=0)
    rng = random.Random()

    async def in_status(status: CustomerStatus, limit: int = 25) -> list:
        page = await list_customers(status, None, None, None, limit, 0)
        result.queries += 1
        return page.items

    async def change(customer, **fields) -> None:
        await update_customer(customer.id, CustomerUpdate(**fields))
        result.status_changes += int("status" in fields)

    with tracer.start_as_current_span("customers.simulate") as span:
        span.set_attribute("customers.simulate.rounds", rounds)
        for round_number in range(rounds):
            status, segment, country, search, offset = rng.choice(BROWSE_QUERIES)
            await list_customers(status, segment, country, search, 10, offset)
            result.queries += 1

            try:
                await create_customer(_prospect(rng))
                result.created += 1
            except DuplicateEmail:
                result.rejected += 1

            leads = await in_status(LEAD)
            if round_number == 0 and leads:
                # Someone fills in the contact form again with an email that's already on file.
                lead = rng.choice(leads)
                try:
                    await create_customer(CustomerCreate(name=lead.name, email=lead.email, company=lead.company,
                                                         segment=lead.segment, country=lead.country))
                except DuplicateEmail:
                    result.rejected += 1
            if leads:
                lead = rng.choice(leads)
                await change(lead, status=ACTIVE, lifetime_value=_deal(rng, lead.segment))

            active = await in_status(ACTIVE, limit=50)
            if active:
                customer = rng.choice(active)
                await change(customer, lifetime_value=customer.lifetime_value + _deal(rng, customer.segment, 0.2))
            if len(active) > 1 and rng.random() < 0.3:
                customer = rng.choice([c for c in active if c.segment != E] or active)
                await change(customer, status=CHURNED)
            if rng.random() < 0.25:
                churned = await in_status(CHURNED)
                if churned:
                    customer = rng.choice(churned)
                    await change(customer, status=ACTIVE,
                                 lifetime_value=customer.lifetime_value + _deal(rng, customer.segment, 0.3))

            if round_number == 0:
                try:
                    # A bookmarked link to an account that was merged away.
                    await get_customer(10_000 + rng.randint(1, 999))
                except CustomerNotFound:
                    result.not_found += 1

        result.total = (await list_customers(None, None, None, None, 1, 0)).total
        span.set_attributes({"customers.simulate.created": result.created,
                             "customers.simulate.status_changes": result.status_changes,
                             "customers.simulate.rejected": result.rejected})
        logger.info(
            "Simulated %d rounds of CRM activity: %d created, %d status changes, %d rejected",
            rounds, result.created, result.status_changes, result.rejected,
            extra={"customers.simulate.rounds": rounds, "customers.simulate.rejected": result.rejected},
        )
        return result
