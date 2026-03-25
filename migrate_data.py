import asyncio, os, sys
from dotenv import load_dotenv
load_dotenv()
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hooks.cost_tracker import CostTracker
from agents.base_agent import BaseAgent, console
import getpass

WORKSPACE = os.path.dirname(os.path.abspath(__file__))

class MigrateAgent(BaseAgent):
    name = "Data Migration"
    emoji = "🔄"
    color = "green"
    ALLOWED_TOOLS = ["Read", "Write", "Bash", "Glob"]
    def get_system_prompt(self):
        return "You are a data migration expert. Migrate data from SQL Server to PostgreSQL."

async def main():
    tracker = CostTracker()
    agent = MigrateAgent(tracker)

    prod_pass = getpass.getpass("Neon PROD DB şifresi: ")
    dev_pass = getpass.getpass("Neon DEV DB şifresi: ")

    prompt = f"""
Migrate data from SQL Server (Docker) to Neon PostgreSQL.

SQL Server connection:
- Host: localhost
- Port: 1433
- User: sa
- Password: TempPass123!
- Database: IONCRM
- Driver: use pymssql (install if needed: pip install pymssql --break-system-packages)

Production Neon:
- Host: ep-purple-sound-a9vyag84-pooler.gwc.azure.neon.tech
- Database: ioncrm
- Username: neondb_owner
- Password: {prod_pass}
- SSL: require

Development Neon:
- Host: ep-royal-grass-a9u9toyt-pooler.gwc.azure.neon.tech
- Database: neondb
- Username: neondb_owner
- Password: {dev_pass}
- SSL: require

MAPPING RULES:

1. PotentialCustomers → Customers (639 records)
   SQL Server        → Neon
   ID                → (skip, use gen_random_uuid())
   CompanyName       → Name
   ContactName       → ContactPerson
   Email             → Email
   Phone             → Phone
   Address           → Address
   CityId            → (skip)
   CreatedOn         → CreatedAt
   CreatedBy         → (set to first SuperAdmin user Id)
   UpdatedOn         → UpdatedAt
   isDeleted         → IsDeleted
   Status = 'Active' (default)
   ProjectId         → (set to first Project Id in Neon)

2. CustomerInterviews → ContactHistories (892 records)
   SQL Server        → Neon
   ID                → (skip, use gen_random_uuid())
   CustomerId        → CustomerId (map from old int ID to new UUID)
   Description       → Notes
   Date              → ContactDate
   Type              → Type (map int to string: 1=Call, 2=Meeting, 3=Email, default=Other)
   CreatedBy         → CreatedByUserId (map to SuperAdmin UUID)
   CreatedOn         → CreatedAt
   UpdatedOn         → UpdatedAt
   isDeleted=false   → IsDeleted=false

3. Users → Users (4 records, skip existing admin@ioncrm.com)
   SQL Server        → Neon
   NameSurname       → FirstName + LastName (split on space)
   Email             → Email
   Password          → PasswordHash (already hashed? if not, use bcrypt)
   isDeleted         → IsDeleted
   CreatedON         → CreatedAt

STEPS:
1. Install pymssql: pip install pymssql --break-system-packages
2. Connect to SQL Server and read all data
3. Connect to Production Neon
4. Check existing data (don't duplicate)
5. Migrate in order: Users → Customers → ContactHistories
6. Print count of migrated records
7. Do the same for Development Neon
8. Report DONE: with counts

Important:
- Use transactions, rollback on error
- Print progress every 100 records
- Skip records that would violate unique constraints
"""
    result = await agent.run(prompt, WORKSPACE)
    tracker.print_summary()
    tracker.save()

if __name__ == "__main__":
    asyncio.run(main())
