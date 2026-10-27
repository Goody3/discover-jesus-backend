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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Page>(entity =>
        {
            entity.HasKey(page => page.Id);
            entity.Property(page => page.Slug).HasMaxLength(200).IsRequired();
            entity.Property(page => page.Title).HasMaxLength(300).IsRequired();
            entity.HasIndex(page => page.Slug).IsUnique();
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
    }
}
