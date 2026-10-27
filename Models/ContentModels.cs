namespace DiscoverJesus.Api.Models;

public sealed class Page
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Title { get; set; }
    public bool IsPublished { get; set; }
    public ICollection<Component> Components { get; set; } = new List<Component>();
}

public sealed class Component
{
    public int Id { get; set; }
    public int PageId { get; set; }
    public required string Key { get; set; }
    public required string Type { get; set; }
    public required string PropsJson { get; set; }
    public int SortOrder { get; set; }
    public Page Page { get; set; } = null!;
    public ICollection<Binding> Bindings { get; set; } = new List<Binding>();
    public ICollection<ActionDefinition> Actions { get; set; } = new List<ActionDefinition>();
}

public sealed class Binding
{
    public int Id { get; set; }
    public int ComponentId { get; set; }
    public required string Name { get; set; }
    public required string Source { get; set; }
    public required string ConfigJson { get; set; }
    public Component Component { get; set; } = null!;
}

public sealed class ActionDefinition
{
    public int Id { get; set; }
    public int ComponentId { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string ConfigJson { get; set; }
    public Component Component { get; set; } = null!;
}
