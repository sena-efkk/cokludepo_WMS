using Microsoft.EntityFrameworkCore;
using Wms.Modules.MasterData.Application;
using Wms.Modules.MasterData.Application.Import;
using Wms.Modules.MasterData.Domain;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Xunit;

namespace Wms.MasterDataTests.Persistence;

public sealed class MasterDataPersistenceTests
{
    private static MasterDataDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseNpgsql(TestConnection.ResolveOrFail(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "master_data"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new MasterDataDbContext(options);
    }

    [Fact]
    public async Task Migrations_apply_and_master_data_schema_contains_expected_tables()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var expected = new[] { "brand", "category", "product", "sku", "sku_barcode", "uom" };
        await using var command = db.Database.GetDbConnection();
        await command.OpenAsync();
        await using var query = command.CreateCommand();
        query.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'master_data'";
        var actual = new List<string>();
        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actual.Add(reader.GetString(0));
        }

        Assert.All(expected, table => Assert.Contains(table, actual));
    }

    [Fact]
    public async Task Uom_seed_data_is_present()
    {
        await using var db = CreateContext();

        var ea = await db.Uoms.FirstOrDefaultAsync(u => u.Code == "EA");

        Assert.NotNull(ea);
        Assert.Equal("Each", ea!.Name);
    }

    [Fact]
    public async Task Sku_roundtrip_persists_and_reads()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var product = Product.Create($"Test Product {suffix}");
        var uom = await db.Uoms.FirstAsync(u => u.Code == "EA");
        var sku = Sku.Create(product.Id, $"T-{suffix}", uom.Id, name: "Test SKU", weightKg: 0.5m);
        sku.AddBarcode($"979{suffix}", BarcodeType.Ean);

        db.Add(product);
        db.Add(sku);
        await db.SaveChangesAsync();

        await using var fresh = CreateContext();
        var loaded = await fresh.Skus
            .Include(s => s.Product)
            .Include(s => s.Barcodes)
            .FirstOrDefaultAsync(s => s.Code == $"T-{suffix}");

        Assert.NotNull(loaded);
        Assert.Equal("Test SKU", loaded!.Name);
        Assert.Equal(0.5m, loaded.WeightKg);
        Assert.Equal("Test Product " + suffix, loaded.Product!.Name);
        Assert.Single(loaded.Barcodes);
        Assert.Equal($"979{suffix}", loaded.Barcodes.First().Value);
    }

    [Fact]
    public async Task Unique_constraint_on_sku_code_is_enforced()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var product = Product.Create($"Unique Test {suffix}");
        var uom = await db.Uoms.FirstAsync(u => u.Code == "EA");
        var first = Sku.Create(product.Id, $"U-{suffix}", uom.Id);
        var second = Sku.Create(product.Id, $"U-{suffix}", uom.Id);

        db.Add(product);
        db.Add(first);
        db.Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Unique_constraint_on_barcode_is_enforced()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var product = Product.Create($"Barcode Test {suffix}");
        var uom = await db.Uoms.FirstAsync(u => u.Code == "EA");
        var first = Sku.Create(product.Id, $"B1-{suffix}", uom.Id);
        var second = Sku.Create(product.Id, $"B2-{suffix}", uom.Id);
        first.AddBarcode($"978{suffix}", BarcodeType.Ean);
        second.AddBarcode($"978{suffix}", BarcodeType.Ean);

        db.Add(product);
        db.Add(first);
        db.Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Sequence_generates_increasing_unique_values()
    {
        await using var db = CreateContext();
        var store = new MasterDataStore(db);

        var first = await store.NextSkuSequenceAsync(CancellationToken.None);
        var second = await store.NextSkuSequenceAsync(CancellationToken.None);

        Assert.True(second > first);
        Assert.NotEqual(SkuCodeGenerator.Format(first), SkuCodeGenerator.Format(second));
    }

    [Fact]
    public async Task Synthetic_import_is_idempotent_on_second_run()
    {
        await using var db = CreateContext();
        var store = new MasterDataStore(db);
        var import = new ImportCatalog(store);
        var catalog = SyntheticCatalogFactory.CreateCatalog();

        var firstRun = await import.Handle(catalog, CancellationToken.None);
        var secondRun = await import.Handle(catalog, CancellationToken.None);

        Assert.Equal(0, secondRun.SkusCreated);
        Assert.Equal(catalog.Count, secondRun.Skipped);

        var syntheticSkus = await db.Skus
            .Where(s => s.Barcodes.Any(b => EF.Functions.Like(b.Value, "999%")))
            .CountAsync();
        Assert.Equal(catalog.Count, syntheticSkus);
    }

    [Fact]
    public async Task Created_skus_via_import_are_valid()
    {
        await using var db = CreateContext();
        var store = new MasterDataStore(db);
        var import = new ImportCatalog(store);

        await import.Handle(SyntheticCatalogFactory.CreateCatalog(), CancellationToken.None);
        var skus = await store.ListSkusAsync(null, null, includeInactive: true, CancellationToken.None);
        var syntheticSkus = skus
            .Where(s => s.Barcodes.Any(b => b.Value.StartsWith("999", StringComparison.Ordinal)))
            .ToList();

        Assert.NotEmpty(syntheticSkus);
        Assert.All(syntheticSkus, sku =>
        {
            Assert.False(string.IsNullOrWhiteSpace(sku.Code));
            Assert.StartsWith("SKU-", sku.Code);
            Assert.NotEmpty(sku.Barcodes);
        });
    }
}
