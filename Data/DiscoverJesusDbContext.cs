using DiscoverJesus.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscoverJesus.Api.Data;

public sealed class DiscoverJesusDbContext(DbContextOptions<DiscoverJesusDbContext> options)
    : DbContext(options)
{
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<Binding> Bindings => Set<Binding>();
    public DbSet<ActionDefinition> Actions => Set<ActionDefinition>();
    public DbSet<ReusableComponentDefinition> ReusableComponents => Set<ReusableComponentDefinition>();
    public DbSet<ReusableComponentDraft> ReusableComponentDrafts => Set<ReusableComponentDraft>();
    public DbSet<ReusableComponentRevision> ReusableComponentRevisions => Set<ReusableComponentRevision>();
    public DbSet<PageContentDraft> PageContentDrafts => Set<PageContentDraft>();
    public DbSet<PageContentRevision> PageContentRevisions => Set<PageContentRevision>();
    public DbSet<PageComponentReference> PageComponentReferences => Set<PageComponentReference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Page>(entity =>
        {
            entity.HasKey(page => page.Id);
            entity.Property(page => page.PublicId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(page => page.Slug).HasMaxLength(200).IsRequired();
            entity.Property(page => page.Title).HasMaxLength(300).IsRequired();
            entity.HasIndex(page => page.Slug).IsUnique();
            entity.HasIndex(page => page.PublicId).IsUnique();
        });

        modelBuilder.Entity<Component>(entity =>
        {
            entity.HasKey(component => component.Id);
            entity.Property(component => component.Key).HasMaxLength(200).IsRequired();
            entity.Property(component => component.Type).HasMaxLength(100).IsRequired();
            entity.Property(component => component.PropsJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.HasIndex(component => new { component.PageId, component.Key }).IsUnique();
            entity.HasOne(component => component.Page)
                .WithMany(page => page.Components)
                .HasForeignKey(component => component.PageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Binding>(entity =>
        {
            entity.HasKey(binding => binding.Id);
            entity.Property(binding => binding.Name).HasMaxLength(200).IsRequired();
            entity.Property(binding => binding.Source).HasMaxLength(500).IsRequired();
            entity.Property(binding => binding.ConfigJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.HasIndex(binding => new { binding.ComponentId, binding.Name }).IsUnique();
            entity.HasOne(binding => binding.Component)
                .WithMany(component => component.Bindings)
                .HasForeignKey(binding => binding.ComponentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ActionDefinition>(entity =>
        {
            entity.ToTable("Actions");
            entity.HasKey(action => action.Id);
            entity.Property(action => action.Name).HasMaxLength(200).IsRequired();
            entity.Property(action => action.Type).HasMaxLength(100).IsRequired();
            entity.Property(action => action.ConfigJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.HasIndex(action => new { action.ComponentId, action.Name }).IsUnique();
            entity.HasOne(action => action.Component)
                .WithMany(component => component.Actions)
                .HasForeignKey(action => action.ComponentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReusableComponentDefinition>(entity =>
        {
            entity.HasKey(definition => definition.Id);
            entity.Property(definition => definition.PublicId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(definition => definition.Name).HasMaxLength(200).IsRequired();
            entity.Property(definition => definition.NormalizedName).HasMaxLength(200).IsRequired();
            entity.Property(definition => definition.CreatedAt).IsRequired();
            entity.HasIndex(definition => definition.PublicId).IsUnique();
            entity.HasIndex(definition => definition.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<ReusableComponentDraft>(entity =>
        {
            entity.HasKey(draft => draft.Id);
            entity.Property(draft => draft.Locale).HasMaxLength(35).IsRequired();
            entity.Property(draft => draft.ContentJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(draft => draft.UpdatedAt).IsRequired();
            entity.HasIndex(draft => new { draft.DefinitionId, draft.Locale }).IsUnique();
            entity.HasOne(draft => draft.Definition)
                .WithMany(definition => definition.Drafts)
                .HasForeignKey(draft => draft.DefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReusableComponentRevision>(entity =>
        {
            entity.HasKey(revision => revision.Id);
            entity.Property(revision => revision.Locale).HasMaxLength(35).IsRequired();
            entity.Property(revision => revision.ContentJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(revision => revision.PublishedAt).IsRequired();
            entity.HasIndex(revision => new { revision.DefinitionId, revision.Locale, revision.RevisionNumber }).IsUnique();
            entity.HasOne(revision => revision.Definition)
                .WithMany(definition => definition.Revisions)
                .HasForeignKey(revision => revision.DefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PageContentDraft>(entity =>
        {
            entity.HasKey(draft => draft.Id);
            entity.Property(draft => draft.Locale).HasMaxLength(35).IsRequired();
            entity.Property(draft => draft.SectionsJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(draft => draft.UpdatedAt).IsRequired();
            entity.HasIndex(draft => new { draft.PageId, draft.Locale }).IsUnique();
            entity.HasOne(draft => draft.Page)
                .WithMany(page => page.ContentDrafts)
                .HasForeignKey(draft => draft.PageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PageContentRevision>(entity =>
        {
            entity.HasKey(revision => revision.Id);
            entity.Property(revision => revision.Locale).HasMaxLength(35).IsRequired();
            entity.Property(revision => revision.SectionsJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(revision => revision.PublishedAt).IsRequired();
            entity.HasIndex(revision => new { revision.PageId, revision.Locale, revision.RevisionNumber }).IsUnique();
            entity.HasOne(revision => revision.Page)
                .WithMany(page => page.ContentRevisions)
                .HasForeignKey(revision => revision.PageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PageComponentReference>(entity =>
        {
            entity.HasKey(reference => reference.Id);
            entity.HasIndex(reference => reference.ComponentPublicId);
            entity.HasOne(reference => reference.Page)
                .WithMany()
                .HasForeignKey(reference => reference.PageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(reference => reference.Draft)
                .WithMany(draft => draft.ComponentReferences)
                .HasForeignKey(reference => reference.PageContentDraftId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(reference => reference.Revision)
                .WithMany(revision => revision.ComponentReferences)
                .HasForeignKey(reference => reference.PageContentRevisionId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_PageComponentReferences_ContentVersion",
                "([PageContentDraftId] IS NULL AND [PageContentRevisionId] IS NOT NULL) OR ([PageContentDraftId] IS NOT NULL AND [PageContentRevisionId] IS NULL)"));
        });
    }
}
