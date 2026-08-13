namespace Wms.Modules.MasterData.Domain;

public sealed class Product : IHasTimestamps
{
    private Product()
    {
        Name = string.Empty;
    }

    private Product(string name, string? description, Guid? brandId, Guid? categoryId)
    {
        Id = Guid.NewGuid();
        Name = name;
        Description = description;
        BrandId = brandId;
        CategoryId = categoryId;
        IsActive = true;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public Guid? BrandId { get; private set; }

    public Guid? CategoryId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public static Product Create(string name, string? description = null, Guid? brandId = null, Guid? categoryId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Product adı boş olamaz.", nameof(name));
        }

        return new Product(name.Trim(), description, brandId, categoryId);
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
