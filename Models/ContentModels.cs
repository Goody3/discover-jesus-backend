namespace DiscoverJesus.Api.Models;

public sealed class Page
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public required string Slug { get; set; }
    public required string Title { get; set; }
    public bool IsPublished { get; set; }
    public ICollection<Component> Components { get; set; } = new List<Component>();
    public ICollection<PageContentDraft> ContentDrafts { get; set; } = new List<PageContentDraft>();
    public ICollection<PageContentRevision> ContentRevisions { get; set; } = new List<PageContentRevision>();
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

public sealed class ReusableComponentDefinition
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public bool IsWithdrawn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<ReusableComponentDraft> Drafts { get; set; } = new List<ReusableComponentDraft>();
    public ICollection<ReusableComponentRevision> Revisions { get; set; } = new List<ReusableComponentRevision>();
}

public sealed class ReusableComponentDraft
{
    public int Id { get; set; }
    public int DefinitionId { get; set; }
    public string Locale { get; set; } = string.Empty;
    public required string ContentJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ReusableComponentDefinition Definition { get; set; } = null!;
}

public sealed class ReusableComponentRevision
{
    public int Id { get; set; }
    public int DefinitionId { get; set; }
    public string Locale { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public required string ContentJson { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public ReusableComponentDefinition Definition { get; set; } = null!;
}

public sealed class PageContentDraft
{
    public int Id { get; set; }
    public int PageId { get; set; }
    public string Locale { get; set; } = string.Empty;
    public required string SectionsJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Page Page { get; set; } = null!;
    public ICollection<PageComponentReference> ComponentReferences { get; set; } = new List<PageComponentReference>();
}

public sealed class PageContentRevision
{
    public int Id { get; set; }
    public int PageId { get; set; }
    public string Locale { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public required string SectionsJson { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public Page Page { get; set; } = null!;
    public ICollection<PageComponentReference> ComponentReferences { get; set; } = new List<PageComponentReference>();
}

public sealed class PageComponentReference
{
    public int Id { get; set; }
    public int PageId { get; set; }
    public Guid ComponentPublicId { get; set; }
    public int? PageContentDraftId { get; set; }
    public int? PageContentRevisionId { get; set; }
    public Page Page { get; set; } = null!;
    public PageContentDraft? Draft { get; set; }
    public PageContentRevision? Revision { get; set; }
}
