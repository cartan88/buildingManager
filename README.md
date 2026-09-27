# Building Manager

Rental property manager for a single Windows PC: properties, units, tenants, leases, automatic monthly rent billing, payments, overdue/aging tracking, numbered billing statements (PDF), expenses with receipts, profit and loss per property, and Excel reports. Amounts are in Philippine pesos (₱).

**Stack:** ASP.NET Core (.NET 10) API · EF Core · SQL Server Express · React + TypeScript (Vite) · ClosedXML (Excel) · QuestPDF (statements)

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

## Billing statements

- Fill in **Settings** first: your name, TIN, address, contact details and payment instructions, all printed on every statement.
- **Create statement** on a lease lists its unpaid charges, arrears included, and you can untick any you want to leave off. **Statements → Issue statements for all** does the monthly run in one go. It skips tenants whose unpaid charges are already all on a live statement, so running it twice doesn't create duplicates.
- **Numbering:** `BS-2026-0001`, `BS-2026-0002`, ... is sequential per year with no gaps, even when statements are issued at the same time. The prefix is configurable.
- **Statements never change once issued.** Everything printed is copied onto the statement and the exact PDF is stored in the database. Editing a tenant or your settings later only affects future statements.
- **Void, don't edit.** A voided statement keeps its number and its stored original. Downloads of it are stamped VOID. To correct one, void it and issue a new one.
- Each statement's payment status (Unpaid / Partially paid / Paid) updates live as payments come in.
- **Statements → Export to Excel** gives the full register, including voided numbers, for your accountant.
- PDFs use the Lato font bundled with QuestPDF, so they look identical on any PC.

## Expenses and profit & loss

- **Expenses** record a date, property (or *General* for costs like accounting fees), category, vendor, amount, payment method and reference, with receipt photos or PDFs attached (up to 10 MB each, stored in the database so backups include them). You can edit an expense to fix a mistake; to remove one, void it.
- **Categories** come pre-loaded for Philippine rentals (repairs, utilities, association dues, real property tax/amilyar, insurance, loan interest, professional fees, permits & licenses, ...). Rename, add or archive them under Settings. *Capital* categories (renovations, furniture & appliances) are shown separately and left out of net income, since they are usually depreciated.
- **Vendors** are created automatically the first time you type a new name. Names match ignoring case.
- **Profit & loss** covers any date range, by property (plus a *General* column) or by month, and exports to Excel with live formulas and an expense detail sheet.
  - **Cash basis** (the default) counts money received in the period, split by what it paid for (rent, utilities billed to tenants, late fees, other). Payments not yet applied to a charge show as *Advance payments*.
  - **Accrual basis** counts charges billed in the period, paid or not.
  - Security deposits are never counted as income.
- The dashboard shows this month's money received, expenses and net income.

## Project layout

```
src/BuildingManager.Core            entities + pure logic (rent schedule, allocation, aging, P&L layout)
src/BuildingManager.Infrastructure  EF Core DbContext & migrations, billing/invoice/expense services, P&L, PDF + Excel output
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

- **Invoices vs. BIR:** a document you present as an official *invoice* must follow BIR rules (registered serial numbers and an ATP, or a registered CAS/CRM system). That's why statements default to the title *Billing Statement* and carry a "not an official receipt or invoice" footer. Only change the title in Settings once your accountant confirms you're registered for it.
- **VAT:** residential leases up to ₱15,000/month per unit are VAT-exempt, but the rules depend on your registration status and total gross receipts. Confirm the thresholds with your accountant.
- **Withholding tax:** corporate tenants usually withhold 5% expanded withholding tax (EWT) on rent and give you BIR Form 2307. Record their payment as the net amount received, plus the 2307 amount, so the charge is fully settled. (A dedicated payment method for this is on the roadmap.)
- **Rent Control Act (RA 9653):** for covered residential units, the deposit is capped at 2 months and advance rent at 1 month.

## Roadmap

1. More Excel reports: rent roll, tenant ledger, annual tax summary
2. Recurring expenses (e.g. monthly association dues)
3. Lease expiry reminders, EWT/2307 tracking, bank CSV import
4. Emailing statements to tenants

## Security

The app has no login and listens on localhost only. Two safeguards protect it from other websites open in your browser:
- Every request that changes data must carry an `X-Requested-With: BuildingManager` header. The React app always sends it; other sites can't ([CrossSiteGuard.cs](src/BuildingManager.Api/CrossSiteGuard.cs)).
- `AllowedHosts` only accepts `localhost`, `127.0.0.1` and `[::1]`, which blocks DNS-rebinding attacks from reading your data. Add a login and change this setting before exposing the app on a network.
