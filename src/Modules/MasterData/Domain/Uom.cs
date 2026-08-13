namespace Wms.Modules.MasterData.Domain;

public sealed class Uom
{
    private Uom()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private Uom(string code, string name)
    {
        Id = Guid.NewGuid();
        Code = code;
        Name = name;
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public static Uom Create(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("UOM kodu boş olamaz.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("UOM adı boş olamaz.", nameof(name));
        }

        return new Uom(code.Trim().ToUpperInvariant(), name.Trim());
    }
}
