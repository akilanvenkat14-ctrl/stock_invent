// ============================================================
//  Stock Inventory Management - Single File ASP.NET Core MVC
//  .NET 8 + EF Core + SQLite
// ============================================================
//  Features:
//  • Dashboard (totals, stock value, low-stock alerts)
//  • Products CRUD + search
//  • Categories CRUD (cannot delete if products assigned)
//  • Stock Transactions (In/Out) with automatic quantity update
//  • Stock-out blocked when it would go negative
//  • Seeded sample data (2 products intentionally below reorder)
// ============================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using StockInventory;

var builder = WebApplication.CreateBuilder(args);

// SQLite database (file will be created next to the executable)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=stockinventory.db"));

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // not strictly needed but harmless

var app = builder.Build();

// Ensure database is created and seeded
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    SeedData.Initialize(db);
}

app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// ============================================================
//  MODELS
// ============================================================

namespace StockInventory;

public class Category
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Description { get; set; }

    public List<Product> Products { get; set; } = new();
}

public class Product
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string SKU { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Range(0, 999999)]
    public decimal UnitPrice { get; set; }

    [Range(0, int.MaxValue)]
    public int QuantityInStock { get; set; }

    [Range(0, int.MaxValue)]
    public int ReorderLevel { get; set; }

    public List<StockTransaction> Transactions { get; set; } = new();
}

public enum TransactionType
{
    StockIn = 1,
    StockOut = 2
}

public class StockTransaction
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public TransactionType Type { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

    [StringLength(250)]
    public string? Notes { get; set; }
}

// ============================================================
//  DbContext
// ============================================================

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>()
            .HasIndex(p => p.SKU)
            .IsUnique();

        modelBuilder.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StockTransaction>()
            .HasOne(t => t.Product)
            .WithMany(p => p.Transactions)
            .HasForeignKey(t => t.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// ============================================================
//  SEED DATA
// ============================================================

public static class SeedData
{
    public static void Initialize(AppDbContext db)
    {
        if (db.Categories.Any()) return;

        var electronics = new Category { Name = "Electronics", Description = "Gadgets and devices" };
        var office = new Category { Name = "Office Supplies", Description = "Stationery and office items" };
        var furniture = new Category { Name = "Furniture", Description = "Desks, chairs, storage" };

        db.Categories.AddRange(electronics, office, furniture);
        db.SaveChanges();

        db.Products.AddRange(
            new Product { SKU = "ELEC-001", Name = "Wireless Mouse", CategoryId = electronics.Id, UnitPrice = 25.99m, QuantityInStock = 45, ReorderLevel = 10 },
            new Product { SKU = "ELEC-002", Name = "USB-C Hub", CategoryId = electronics.Id, UnitPrice = 39.50m, QuantityInStock = 8, ReorderLevel = 15 },   // low stock
            new Product { SKU = "OFF-001", Name = "A4 Paper Ream", CategoryId = office.Id, UnitPrice = 6.75m, QuantityInStock = 120, ReorderLevel = 20 },
            new Product { SKU = "OFF-002", Name = "Ballpoint Pens (Box)", CategoryId = office.Id, UnitPrice = 4.20m, QuantityInStock = 3, ReorderLevel = 10 }, // low stock
            new Product { SKU = "FURN-001", Name = "Ergonomic Chair", CategoryId = furniture.Id, UnitPrice = 189.00m, QuantityInStock = 12, ReorderLevel = 5 },
            new Product { SKU = "FURN-002", Name = "Standing Desk", CategoryId = furniture.Id, UnitPrice = 349.00m, QuantityInStock = 6, ReorderLevel = 3 }
        );
        db.SaveChanges();
    }
}

// ============================================================
//  CONTROLLERS + INLINE HTML HELPERS
// ============================================================

public class HomeController : Controller
{
    private readonly AppDbContext _db;
    public HomeController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var products = await _db.Products.Include(p => p.Category).ToListAsync();
        var totalProducts = products.Count;
        var totalUnits = products.Sum(p => p.QuantityInStock);
        var stockValue = products.Sum(p => p.QuantityInStock * p.UnitPrice);
        var lowStock = products.Where(p => p.QuantityInStock <= p.ReorderLevel)
                               .OrderBy(p => p.QuantityInStock)
                               .ToList();

        ViewBag.TotalProducts = totalProducts;
        ViewBag.TotalUnits = totalUnits;
        ViewBag.StockValue = stockValue;
        ViewBag.LowStock = lowStock;

        return View("Dashboard");
    }
}

public class ProductsController : Controller
{
    private readonly AppDbContext _db;
    public ProductsController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? search)
    {
        var query = _db.Products.Include(p => p.Category).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(search) ||
                p.SKU.ToLower().Contains(search) ||
                (p.Category != null && p.Category.Name.ToLower().Contains(search)));
        }
        var list = await query.OrderBy(p => p.Name).ToListAsync();
        ViewBag.Search = search;
        return View(list);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
        return View(new Product());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Product model)
    {
        if (ModelState.IsValid)
        {
            if (await _db.Products.AnyAsync(p => p.SKU == model.SKU))
            {
                ModelState.AddModelError("SKU", "SKU already exists.");
            }
            else
            {
                _db.Products.Add(model);
                await _db.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
        }
        ViewBag.Categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
        return View(model);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound();
        ViewBag.Categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Product model)
    {
        if (id != model.Id) return BadRequest();
        if (ModelState.IsValid)
        {
            var exists = await _db.Products.AnyAsync(p => p.SKU == model.SKU && p.Id != id);
            if (exists)
            {
                ModelState.AddModelError("SKU", "SKU already exists.");
            }
            else
            {
                _db.Update(model);
                await _db.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
        }
        ViewBag.Categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
        return View(model);
    }

    public async Task<IActionResult> Delete(int id)
    {
        var product = await _db.Products.Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound();
        return View(product);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product != null)
        {
            _db.Products.Remove(product);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}

public class CategoriesController : Controller
{
    private readonly AppDbContext _db;
    public CategoriesController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var list = await _db.Categories
            .Include(c => c.Products)
            .OrderBy(c => c.Name)
            .ToListAsync();
        return View(list);
    }

    public IActionResult Create() => View(new Category());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Category model)
    {
        if (ModelState.IsValid)
        {
            _db.Categories.Add(model);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(model);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var cat = await _db.Categories.FindAsync(id);
        if (cat == null) return NotFound();
        return View(cat);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Category model)
    {
        if (id != model.Id) return BadRequest();
        if (ModelState.IsValid)
        {
            _db.Update(model);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(model);
    }

    public async Task<IActionResult> Delete(int id)
    {
        var cat = await _db.Categories.Include(c => c.Products).FirstOrDefaultAsync(c => c.Id == id);
        if (cat == null) return NotFound();
        return View(cat);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var cat = await _db.Categories.Include(c => c.Products).FirstOrDefaultAsync(c => c.Id == id);
        if (cat == null) return NotFound();

        if (cat.Products.Any())
        {
            TempData["Error"] = "Cannot delete category because it has products assigned.";
            return RedirectToAction(nameof(Index));
        }

        _db.Categories.Remove(cat);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}

public class StockTransactionsController : Controller
{
    private readonly AppDbContext _db;
    public StockTransactionsController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var list = await _db.StockTransactions
            .Include(t => t.Product)
            .OrderByDescending(t => t.TransactionDate)
            .Take(100)
            .ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Products = await _db.Products.OrderBy(p => p.Name).ToListAsync();
        return View(new StockTransaction());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StockTransaction model)
    {
        if (ModelState.IsValid)
        {
            var product = await _db.Products.FindAsync(model.ProductId);
            if (product == null)
            {
                ModelState.AddModelError("ProductId", "Product not found.");
            }
            else if (model.Type == TransactionType.StockOut && product.QuantityInStock < model.Quantity)
            {
                ModelState.AddModelError("Quantity",
                    $"Insufficient stock. Available: {product.QuantityInStock}");
            }
            else
            {
                // Update product quantity
                if (model.Type == TransactionType.StockIn)
                    product.QuantityInStock += model.Quantity;
                else
                    product.QuantityInStock -= model.Quantity;

                model.TransactionDate = DateTime.UtcNow;
                _db.StockTransactions.Add(model);
                await _db.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
        }
        ViewBag.Products = await _db.Products.OrderBy(p => p.Name).ToListAsync();
        return View(model);
    }
}

// ============================================================
//  NOTE ABOUT VIEWS
// ============================================================
// Because this is a true single-file application, the Razor views
// are provided below as separate .cshtml files you should place
// in the Views folder.  Create the following structure:
//
// Views/
//   Shared/
//     _Layout.cshtml
//     _ViewImports.cshtml
//     _ViewStart.cshtml
//   Home/
//     Dashboard.cshtml
//   Products/
//     Index.cshtml
//     Create.cshtml
//     Edit.cshtml
//     Delete.cshtml
//   Categories/
//     Index.cshtml
//     Create.cshtml
//     Edit.cshtml
//     Delete.cshtml
//   StockTransactions/
//     Index.cshtml
//     Create.cshtml
//
// Copy each view content exactly as shown in the next section.
// ============================================================
