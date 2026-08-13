namespace Wms.Modules.MasterData.Domain;

public sealed class Category
{
    private Category()
    {
        Name = string.Empty;
    }

    private Category(string name)
    {
        Id = Guid.NewGuid();
        Name = name;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public static Category Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category adı boş olamaz.", nameof(name));
        }

        return new Category(name.Trim());
    }
}
