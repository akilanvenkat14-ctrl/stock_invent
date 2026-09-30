# Stock Inventory Management

Full ASP.NET Core MVC application (.NET 8 + EF Core + SQLite) for managing stock inventory.

## Features

- **Dashboard** — Total products, units in stock, stock value, and low-stock alert table
- **Products** — Full CRUD with SKU, category, price, quantity, reorder level, and search
- **Categories** — Full CRUD (blocked from deleting if products are assigned)
- **Stock Transactions** — Record Stock In / Stock Out; product quantities update automatically. Stock-out is blocked if it would go negative
- Seeded with sample categories & products (two intentionally below reorder level so the alert is visible immediately)

## How to Run

1. Make sure you have **.NET 8 SDK** installed.
2. Open a terminal in this folder and run:

```bash
dotnet restore
dotnet run
```

3. Open the URL shown in the console (usually `https://localhost:5xxx` or `http://localhost:5xxx`).

The SQLite database file `stockinventory.db` will be created automatically on first run.

## Project Structure

- `Program.cs` — Everything in one file: models, DbContext, seed data, and all controllers
- `Views/` — Razor views (Dashboard, Products, Categories, Stock Transactions)
- Bootstrap 5 + Bootstrap Icons for a clean modern UI

## Notes

- Categories cannot be deleted while they still have products.
- Stock-out transactions are rejected when quantity would go negative.
- Two sample products start below their reorder level so you immediately see the low-stock alerts on the dashboard.
