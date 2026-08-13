namespace Wms.Modules.MasterData.Domain;

public sealed class Brand
{
    private Brand()
    {
        Name = string.Empty;
    }

    private Brand(string name)
    {
        Id = Guid.NewGuid();
        Name = name;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public static Brand Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Brand adı boş olamaz.", nameof(name));
        }

        return new Brand(name.Trim());
    }
}
