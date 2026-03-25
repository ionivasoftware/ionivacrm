#!/usr/bin/env python3
"""
SQL Server → Neon PostgreSQL Migration Script
Migrates: Users → (Project) → Customers → ContactHistories

Actual Neon schema discovered by inspection:
  Users          : Id, Email, PasswordHash, FirstName, LastName,
                   IsSuperAdmin, IsActive, CreatedAt, UpdatedAt, IsDeleted
  Projects       : Id, Name, Description, IsActive, CreatedAt, UpdatedAt, IsDeleted
  Customers      : Id, ProjectId, Code, CompanyName, ContactName, Email,
                   Phone, Address, TaxNumber, TaxUnit, Status(int), Segment,
                   AssignedUserId, LegacyId, CreatedAt, UpdatedAt, IsDeleted
  ContactHistories: Id, CustomerId, ProjectId, Type(int), Subject, Content,
                    Outcome, ContactedAt, CreatedByUserId, LegacyId,
                    CreatedAt, UpdatedAt, IsDeleted

Enums:
  CustomerStatus : Lead=1, Active=2, Inactive=3, Churned=4
  ContactType    : Call=1, Email=2, Meeting=3, Note=4, WhatsApp=5, Visit=6
"""

import sys
import uuid
import bcrypt
import pymssql
import psycopg2
import psycopg2.extras
from datetime import datetime, timezone

# ── Connection config ──────────────────────────────────────────────────────────

MSSQL = dict(
    server="localhost",
    port=1433,
    user="sa",
    password="TempPass123!",
    database="IONCRM",
)

NEON_PROD = dict(
    host="ep-purple-sound-a9vyag84-pooler.gwc.azure.neon.tech",
    dbname="ioncrm",
    user="neondb_owner",
    password="npg_m4jsHwGxF6Jz",
    sslmode="require",
)

NEON_DEV = dict(
    host="ep-royal-grass-a9u9toyt-pooler.gwc.azure.neon.tech",
    dbname="neondb",
    user="neondb_owner",
    password="npg_5jEWJRQGo2fH",
    sslmode="require",
)

# SQL Server CustomerInterview.Type → Neon ContactType enum
# SQL Server: 1=Call, 2=Meeting, 3=Email
# Neon:       Call=1, Email=2, Meeting=3, Note=4
CONTACT_TYPE_MAP = {1: 1, 2: 3, 3: 2}   # SS→Neon

NOW_UTC = datetime.now(timezone.utc)

# ── Helpers ────────────────────────────────────────────────────────────────────

def log(msg):
    print(f"[{datetime.now().strftime('%H:%M:%S')}] {msg}", flush=True)


def split_name(full_name):
    if not full_name:
        return "", ""
    parts = str(full_name).strip().split(" ", 1)
    return (parts[0], parts[1]) if len(parts) == 2 else (parts[0], "")


def looks_hashed(pw: str) -> bool:
    return isinstance(pw, str) and pw.startswith("$2")


def hash_password(raw: str) -> str:
    return bcrypt.hashpw(raw.encode(), bcrypt.gensalt()).decode()


def trunc(val, max_len):
    if val is None:
        return None
    s = str(val).strip()
    return s[:max_len] if s else None


def coalesce(*args):
    for a in args:
        if a is not None:
            return a
    return NOW_UTC


# ── SQL Server reader ──────────────────────────────────────────────────────────

def read_mssql():
    log("Connecting to SQL Server …")
    conn = pymssql.connect(**MSSQL)
    cur = conn.cursor(as_dict=True)

    log("Reading Users …")
    cur.execute("SELECT * FROM Users")
    users = cur.fetchall()

    log("Reading PotentialCustomers …")
    cur.execute("SELECT * FROM PotentialCustomers")
    customers = cur.fetchall()

    log("Reading CustomerInterviews …")
    cur.execute("SELECT * FROM CustomerInterviews")
    interviews = cur.fetchall()

    cur.close()
    conn.close()

    log(f"  → {len(users)} users | {len(customers)} customers | {len(interviews)} interviews")
    return users, customers, interviews


# ── Neon migrator ──────────────────────────────────────────────────────────────

def migrate_to_neon(pg_cfg: dict, label: str, users, customers, interviews):
    log(f"\n{'='*60}")
    log(f"Starting migration → {label}")
    log(f"{'='*60}")

    conn = psycopg2.connect(**pg_cfg, connect_timeout=15)
    conn.autocommit = False
    cur = conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)

    counts = {"users": 0, "projects": 0, "customers": 0, "contact_histories": 0}

    try:
        # ── 0. Resolve SuperAdmin ──────────────────────────────────────────────
        log("Fetching SuperAdmin user …")
        cur.execute("""
            SELECT "Id" FROM "Users"
            WHERE "IsSuperAdmin" = TRUE
            ORDER BY "CreatedAt"
            LIMIT 1
        """)
        row = cur.fetchone()
        if not row:
            raise RuntimeError("No SuperAdmin (IsSuperAdmin=TRUE) found in Neon — cannot continue.")
        super_admin_id = str(row["Id"])
        log(f"  SuperAdmin Id = {super_admin_id}")

        # ── 1. Users ──────────────────────────────────────────────────────────
        log(f"\n--- Migrating Users ({len(users)} source records) ---")

        cur.execute('SELECT "Email" FROM "Users"')
        existing_emails = {r["Email"] for r in cur.fetchall()}

        user_batch = []
        for u in users:
            email = (u.get("Email") or "").strip()
            if not email or email in existing_emails:
                log(f"  SKIP user: {email or '(no email)'}")
                continue

            pw = u.get("Password") or u.get("PasswordHash") or ""
            pw_hash = pw if looks_hashed(pw) else hash_password(pw or "ChangeMe123!")
            first, last = split_name(u.get("NameSurname") or "")
            created_at = coalesce(u.get("CreatedON"), u.get("CreatedOn"), NOW_UTC)
            is_super = bool(u.get("IsSuperAdmin", False))

            user_batch.append((
                str(uuid.uuid4()),  # Id
                email,              # Email
                pw_hash,            # PasswordHash
                first or "Unknown", # FirstName
                last or "",         # LastName
                is_super,           # IsSuperAdmin
                True,               # IsActive
                created_at,         # CreatedAt
                created_at,         # UpdatedAt (reuse CreatedAt if no UpdatedOn)
                bool(u.get("isDeleted", False)),  # IsDeleted
            ))
            existing_emails.add(email)

        if user_batch:
            psycopg2.extras.execute_values(cur, """
                INSERT INTO "Users"
                    ("Id","Email","PasswordHash","FirstName","LastName",
                     "IsSuperAdmin","IsActive","CreatedAt","UpdatedAt","IsDeleted")
                VALUES %s
                ON CONFLICT ("Email") DO NOTHING
            """, user_batch)
            counts["users"] = len(user_batch)
            log(f"  ✓ Inserted {counts['users']} users")
        else:
            log("  ✓ No new users to insert")

        # ── 2. Project (create default if none exists) ────────────────────────
        log("\n--- Resolving Project ---")
        cur.execute('SELECT "Id" FROM "Projects" WHERE "IsDeleted" = FALSE ORDER BY "CreatedAt" LIMIT 1')
        row = cur.fetchone()
        if row:
            project_id = str(row["Id"])
            log(f"  Using existing Project Id = {project_id}")
        else:
            project_id = str(uuid.uuid4())
            cur.execute("""
                INSERT INTO "Projects"
                    ("Id","Name","Description","IsActive","CreatedAt","UpdatedAt","IsDeleted")
                VALUES (%s, %s, %s, %s, %s, %s, %s)
            """, (project_id, "IONCRM Migration", "Imported from SQL Server legacy system",
                  True, NOW_UTC, NOW_UTC, False))
            counts["projects"] = 1
            log(f"  ✓ Created default Project Id = {project_id}")

        # ── 3. Customers ──────────────────────────────────────────────────────
        log(f"\n--- Migrating Customers ({len(customers)} source records) ---")

        # Build legacy ID set from existing records to avoid duplicates
        cur.execute('SELECT "LegacyId" FROM "Customers" WHERE "LegacyId" IS NOT NULL')
        existing_legacy = {r["LegacyId"] for r in cur.fetchall()}

        # old int ID → new UUID map (for ContactHistory FK)
        old_cust_id_to_uuid: dict[int, str] = {}

        # Also pre-load UUIDs for already-migrated customers
        cur.execute('SELECT "Id", "LegacyId" FROM "Customers" WHERE "LegacyId" IS NOT NULL')
        for r in cur.fetchall():
            try:
                legacy = r["LegacyId"]
                if legacy and legacy.startswith("PC-"):
                    old_id = int(legacy[3:])
                    old_cust_id_to_uuid[old_id] = str(r["Id"])
            except (ValueError, TypeError):
                pass

        cust_batch = []
        for idx, c in enumerate(customers, 1):
            old_id = c.get("ID")
            legacy_id = f"PC-{old_id}"

            if legacy_id in existing_legacy:
                # Already exists — just record its UUID for FK mapping
                if old_id and old_id not in old_cust_id_to_uuid:
                    cur.execute('SELECT "Id" FROM "Customers" WHERE "LegacyId" = %s', (legacy_id,))
                    existing_row = cur.fetchone()
                    if existing_row:
                        old_cust_id_to_uuid[old_id] = str(existing_row["Id"])
                log(f"  SKIP customer (LegacyId exists): {legacy_id}")
                continue

            new_uuid = str(uuid.uuid4())
            old_cust_id_to_uuid[old_id] = new_uuid

            created_at = coalesce(c.get("CreatedOn"), NOW_UTC)
            updated_at = coalesce(c.get("UpdatedOn"), created_at)

            cust_batch.append((
                new_uuid,                             # Id
                project_id,                           # ProjectId
                None,                                 # Code
                trunc(c.get("CompanyName"), 300) or "Unknown",  # CompanyName
                trunc(c.get("ContactName"), 200),     # ContactName
                trunc(c.get("Email"), 256),           # Email
                trunc(c.get("Phone"), 50),            # Phone
                trunc(c.get("Address"), 500),         # Address
                None,                                 # TaxNumber
                None,                                 # TaxUnit
                2,                                    # Status = Active(2)
                None,                                 # Segment
                None,                                 # AssignedUserId
                legacy_id,                            # LegacyId
                created_at,                           # CreatedAt
                updated_at,                           # UpdatedAt
                bool(c.get("isDeleted", False)),      # IsDeleted
            ))
            existing_legacy.add(legacy_id)

            if idx % 100 == 0:
                log(f"  … prepared {idx}/{len(customers)} customers")

        if cust_batch:
            psycopg2.extras.execute_values(cur, """
                INSERT INTO "Customers"
                    ("Id","ProjectId","Code","CompanyName","ContactName","Email",
                     "Phone","Address","TaxNumber","TaxUnit","Status","Segment",
                     "AssignedUserId","LegacyId","CreatedAt","UpdatedAt","IsDeleted")
                VALUES %s
                ON CONFLICT DO NOTHING
            """, cust_batch, page_size=200)
            counts["customers"] = len(cust_batch)
            log(f"  ✓ Inserted {counts['customers']} customers")
        else:
            log("  ✓ No new customers to insert")

        # ── 4. ContactHistories ───────────────────────────────────────────────
        log(f"\n--- Migrating ContactHistories ({len(interviews)} source records) ---")

        cur.execute('SELECT "LegacyId" FROM "ContactHistories" WHERE "LegacyId" IS NOT NULL')
        existing_ch_legacy = {r["LegacyId"] for r in cur.fetchall()}

        ch_batch = []
        skipped_ch = 0
        for idx, iv in enumerate(interviews, 1):
            old_id   = iv.get("ID")
            legacy_id = f"CI-{old_id}"

            if legacy_id in existing_ch_legacy:
                log(f"  SKIP contact history (LegacyId exists): {legacy_id}")
                continue

            old_cust_id = iv.get("CustomerId")
            new_cust_uuid = old_cust_id_to_uuid.get(old_cust_id)
            if not new_cust_uuid:
                skipped_ch += 1
                continue  # orphan FK — skip

            # Type mapping: SS(1=Call,2=Meeting,3=Email) → Neon enum
            type_int = CONTACT_TYPE_MAP.get(iv.get("Type"), 4)  # default Note=4

            contact_date = iv.get("Date")
            created_at   = coalesce(iv.get("CreatedOn"), NOW_UTC)
            updated_at   = coalesce(iv.get("UpdatedOn"), created_at)

            description  = trunc(iv.get("Description"), 2000)

            ch_batch.append((
                str(uuid.uuid4()),          # Id
                new_cust_uuid,              # CustomerId
                project_id,                 # ProjectId
                type_int,                   # Type (int)
                None,                       # Subject
                description,               # Content
                None,                       # Outcome
                contact_date or created_at, # ContactedAt
                super_admin_id,             # CreatedByUserId
                legacy_id,                  # LegacyId
                created_at,                 # CreatedAt
                updated_at,                 # UpdatedAt
                False,                      # IsDeleted
            ))
            existing_ch_legacy.add(legacy_id)

            if idx % 100 == 0:
                log(f"  … prepared {idx}/{len(interviews)} contact histories")

        if ch_batch:
            psycopg2.extras.execute_values(cur, """
                INSERT INTO "ContactHistories"
                    ("Id","CustomerId","ProjectId","Type","Subject","Content","Outcome",
                     "ContactedAt","CreatedByUserId","LegacyId","CreatedAt","UpdatedAt","IsDeleted")
                VALUES %s
                ON CONFLICT DO NOTHING
            """, ch_batch, page_size=200)
            counts["contact_histories"] = len(ch_batch)
            log(f"  ✓ Inserted {counts['contact_histories']} contact histories "
                f"(skipped {skipped_ch} orphans)")
        else:
            log("  ✓ No new contact histories to insert")

        conn.commit()
        log(f"\n✅ {label} — committed successfully.")

    except Exception as exc:
        conn.rollback()
        log(f"\n❌ ERROR in {label} — rolled back.")
        import traceback
        traceback.print_exc()
        raise
    finally:
        cur.close()
        conn.close()

    return counts


# ── Entry point ────────────────────────────────────────────────────────────────

def main():
    log("=== SQL Server → Neon PostgreSQL Migration ===\n")

    users, customers, interviews = read_mssql()

    prod_counts = migrate_to_neon(NEON_PROD, "PRODUCTION (ioncrm)", users, customers, interviews)
    dev_counts  = migrate_to_neon(NEON_DEV,  "DEVELOPMENT (neondb)", users, customers, interviews)

    print("\n" + "="*60)
    print("DONE:")
    print(f"  PRODUCTION  — users: {prod_counts['users']:>4}  "
          f"projects: {prod_counts['projects']}  "
          f"customers: {prod_counts['customers']:>4}  "
          f"contact_histories: {prod_counts['contact_histories']:>4}")
    print(f"  DEVELOPMENT — users: {dev_counts['users']:>4}  "
          f"projects: {dev_counts['projects']}  "
          f"customers: {dev_counts['customers']:>4}  "
          f"contact_histories: {dev_counts['contact_histories']:>4}")
    print("="*60)


if __name__ == "__main__":
    main()
