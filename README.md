# Building Manager

Rental property manager for a single Windows PC: properties, units, tenants, leases, automatic monthly rent billing, payments, overdue/aging tracking and Excel reports. Amounts are in Philippine pesos (₱).

**Stack:** ASP.NET Core (.NET 10) API · EF Core · SQL Server Express · React + TypeScript (Vite) · ClosedXML (Excel) · QuestPDF (invoices, next up)

## Running it

Prerequisites: .NET 10 SDK, Node 20+, SQL Server Express at `.\SQLEXPRESS` (change `ConnectionStrings:BuildingManager` in `src/BuildingManager.Api/appsettings.json` otherwise). The database is created and migrated automatically on startup.

**Everyday use** (one process serves the API and the UI):

```bash
cd src/web && npm install && npm run build
dotnet run --project src/BuildingManager.Api --launch-profile http
```

Then open http://localhost:5073.

**Development** (hot reload): run the API as above, and in a second terminal run `npm run dev` in `src/web`, then open http://localhost:5173 (`/api` is proxied to the API).

**Tests:**
- `dotnet test`: unit tests for the billing logic, plus integration and API tests. The integration tests create a throwaway database on `.\SQLEXPRESS` for each test and drop it afterwards. Set `BUILDINGMANAGER_TEST_SQL` to use a different server.
- `npm test` (in `src/web`): frontend unit tests (Vitest).

## How the money works

- **Charges** are what a tenant owes (rent, utilities, other). Rent charges are created automatically on each lease's due date by a background job that runs at startup and hourly. It is idempotent: a unique index on (lease, period) means a period can never be billed twice.
- **Payments** are applied to the oldest unpaid charges first. Overpayments stay as tenant credit and are used up automatically by the next charge.
- **Overdue** is never stored. It is calculated from unpaid charges whose due date plus the lease's grace period has passed. Aging buckets: 1–30, 31–60, 61–90 and 90+ days from the due date.
- **Nothing financial is deleted.** Charges and payments are *voided* (kept on record, excluded from balances), and the money is re-applied.
- **Security deposits** are recorded on the lease as money held, not income.
- **Ending a lease** (or shortening it) voids rent already billed for periods that start after the end date, and re-applies any money paid towards them. If the lease is later extended, those periods are billed again. Rent you voided by hand stays voided.
- **"As of" reports** replay payments using only money received by that date, so month-end reports for past months stay accurate.
- **Concurrency:** all billing work on a lease runs in a transaction holding a SQL Server app lock for that lease, so the background job and your own actions can't apply the same money twice.
- Rent due on the 29th–31st moves to the last day of shorter months.
- Changing a lease's rent affects future charges only.

## Project layout

```
src/BuildingManager.Core            entities + pure billing logic (rent schedule, allocation, aging)
src/BuildingManager.Infrastructure  EF Core DbContext & migrations, billing service, reports, Excel export
src/BuildingManager.Api             minimal-API endpoints, rent worker, serves the React build from wwwroot
src/web                             React UI
tests/BuildingManager.Tests         xUnit tests for the billing logic
scripts/backup-database.ps1         nightly .bak backup (see comments for Task Scheduler setup)
```

Adding a migration after changing entities:

```bash
dotnet ef migrations add <Name> -p src/BuildingManager.Infrastructure -s src/BuildingManager.Api -o Data/Migrations
```

## Philippines notes

- **Invoices vs. BIR:** a document you present as an official *invoice* must follow BIR rules (registered serial numbers and an ATP, or a registered CAS/CRM system). Until that's set up, generated PDFs should be titled *Billing Statement* / *Statement of Account*. Confirm with your accountant.
- **VAT:** residential leases up to ₱15,000/month per unit are VAT-exempt, but the rules depend on your registration status and total gross receipts. Confirm the thresholds with your accountant.
- **Withholding tax:** corporate tenants usually withhold 5% expanded withholding tax (EWT) on rent and give you BIR Form 2307. Record their payment as the net amount received, plus the 2307 amount, so the charge is fully settled. (A dedicated payment method for this is on the roadmap.)
- **Rent Control Act (RA 9653):** for covered residential units, the deposit is capped at 2 months and advance rent at 1 month.

## Roadmap

1. Billing statements / invoices (QuestPDF), numbered sequentially and never changed once issued
2. Expenses (vendors, categories, receipts) and profit and loss per property
3. More Excel reports: rent roll, tenant ledger, monthly income, annual tax summary
4. Lease expiry reminders, EWT/2307 tracking, bank CSV import

## Security

The app has no login and listens on localhost only. Two safeguards protect it from other websites open in your browser:
- Every request that changes data must carry an `X-Requested-With: BuildingManager` header. The React app always sends it; other sites can't ([CrossSiteGuard.cs](src/BuildingManager.Api/CrossSiteGuard.cs)).
- `AllowedHosts` only accepts `localhost`, `127.0.0.1` and `[::1]`, which blocks DNS-rebinding attacks from reading your data. Add a login and change this setting before exposing the app on a network.
