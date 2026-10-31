namespace Rabbictyl.Core.Entities;

public sealed class EggVariable : Entity
{
    public Guid EggId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string EnvVariable { get; set; } = string.Empty;

    public string DefaultValue { get; set; } = string.Empty;

    public bool UserViewable { get; set; } = true;

    public bool UserEditable { get; set; } = true;

    public string Rules { get; set; } = "string";
}
