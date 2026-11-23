using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using DiscoverJesus.Api.Contracts;
using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using DiscoverJesus.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DiscoverJesus.Api;

public static class ContentEndpoints
{
    public static IEndpointRouteBuilder MapContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api")
            .RequireRateLimiting("authoring")
            .RequireAuthorization("Author")
            .RequireCsrfProtection();
        var pages = api.MapGroup("/pages").WithTags("Pages");
        pages.MapGet("/", GetPages).WithName("GetPages").WithSummary("List pages")
            .Produces<IReadOnlyList<PageSummary>>();
        pages.MapGet("/by-public-id/{pageId:guid}", GetPageByPublicId)
            .WithName("GetPageByPublicId")
            .WithSummary("Get a management page by public UUID, including unpublished pages")
            .Produces<PageResponse>()
            .Produces(StatusCodes.Status404NotFound);
        pages.MapPost("/", CreatePage).RequireAuthorization("Publisher")
            .WithName("CreatePage").WithSummary("Create a page")
            .Produces<PageSummary>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);
        pages.MapPut("/{id:int}", UpdatePage).RequireAuthorization("Publisher")
            .WithName("UpdatePage").WithSummary("Update a page")
            .Produces<PageSummary>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        pages.MapDelete("/{id:int}", DeletePage).RequireAuthorization("Publisher")
            .WithName("DeletePage").WithSummary("Delete a page")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        pages.MapGet("/{pageId:int}/components", GetComponents).WithName("GetComponents")
            .WithSummary("List a page's components")
            .Produces<IReadOnlyList<ComponentResponse>>()
            .Produces(StatusCodes.Status404NotFound);
        pages.MapPost("/{pageId:int}/components", CreateComponent).RequireAuthorization("Publisher")
            .WithName("CreateComponent")
            .WithSummary("Add a component to a page")
            .Produces<ComponentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        api.MapGet("/components/{id:int}", GetComponent).WithTags("Components")
            .WithName("GetComponent").WithSummary("Get a component")
            .Produces<ComponentResponse>()
            .Produces(StatusCodes.Status404NotFound);
        api.MapPut("/components/{id:int}", UpdateComponent).RequireAuthorization("Publisher")
            .WithTags("Components")
            .WithName("UpdateComponent").WithSummary("Update a component")
            .Produces<ComponentResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        api.MapDelete("/components/{id:int}", DeleteComponent).RequireAuthorization("Publisher")
            .WithTags("Components")
            .WithName("DeleteComponent").WithSummary("Delete a component")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        api.MapGet("/components/{componentId:int}/bindings", GetBindings)
            .WithTags("Bindings").WithName("GetBindings").WithSummary("List component bindings")
            .Produces<IReadOnlyList<BindingResponse>>()
            .Produces(StatusCodes.Status404NotFound);
        api.MapPost("/components/{componentId:int}/bindings", CreateBinding).RequireAuthorization("Publisher")
            .WithTags("Bindings").WithName("CreateBinding").WithSummary("Add a component binding")
            .Produces<BindingResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        api.MapGet("/bindings/{id:int}", GetBinding).WithTags("Bindings")
            .WithName("GetBinding").WithSummary("Get a binding")
            .Produces<BindingResponse>()
            .Produces(StatusCodes.Status404NotFound);
        api.MapPut("/bindings/{id:int}", UpdateBinding).RequireAuthorization("Publisher")
            .WithTags("Bindings")
            .WithName("UpdateBinding").WithSummary("Update a binding")
            .Produces<BindingResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        api.MapDelete("/bindings/{id:int}", DeleteBinding).RequireAuthorization("Publisher")
            .WithTags("Bindings")
            .WithName("DeleteBinding").WithSummary("Delete a binding")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        api.MapGet("/components/{componentId:int}/actions", GetActions)
            .WithTags("Actions").WithName("GetActions").WithSummary("List component actions")
            .Produces<IReadOnlyList<ActionResponse>>()
            .Produces(StatusCodes.Status404NotFound);
        api.MapPost("/components/{componentId:int}/actions", CreateAction).RequireAuthorization("Publisher")
            .WithTags("Actions").WithName("CreateAction").WithSummary("Add a component action")
            .Produces<ActionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        api.MapGet("/actions/{id:int}", GetAction).WithTags("Actions")
            .WithName("GetAction").WithSummary("Get an action definition")
            .Produces<ActionResponse>()
            .Produces(StatusCodes.Status404NotFound);
        api.MapPut("/actions/{id:int}", UpdateAction).RequireAuthorization("Publisher")
            .WithTags("Actions")
            .WithName("UpdateAction").WithSummary("Update an action definition")
            .Produces<ActionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        api.MapDelete("/actions/{id:int}", DeleteAction).RequireAuthorization("Publisher")
            .WithTags("Actions")
            .WithName("DeleteAction").WithSummary("Delete an action definition")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetPages(DiscoverJesusDbContext db, CancellationToken cancellationToken)
    {
        var pages = await db.Pages.AsNoTracking()
            .OrderBy(page => page.Slug)
            .Select(page => new PageSummary(
                page.Id,
                page.PublicId,
                page.Slug,
                page.Title,
                page.IsPublished))
            .ToListAsync(cancellationToken);
        return Results.Ok(pages);
    }

    private static async Task<IResult> GetPageByPublicId(
        Guid pageId,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        var page = await PageDetails(db)
            .SingleOrDefaultAsync(candidate => candidate.PublicId == pageId, cancellationToken);
        return page is null ? Results.NotFound() : Results.Ok(ToResponse(page));
    }

    private static IQueryable<Page> PageDetails(DiscoverJesusDbContext db) =>
        db.Pages.AsNoTracking()
            .Include(page => page.Components)
                .ThenInclude(component => component.Bindings)
            .Include(page => page.Components)
                .ThenInclude(component => component.Actions);

    private static async Task<IResult> CreatePage(
        PageRequest request,
        DiscoverJesusDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var validation = ValidatePage(request);
        if (validation is not null)
        {
            return validation;
        }

        if (await db.Pages.AnyAsync(page => page.Slug == request.Slug, cancellationToken))
        {
            return Results.Conflict(new { error = "A page with this slug already exists." });
        }

        var page = new Page
        {
            Slug = request.Slug,
            Title = request.Title,
            IsPublished = request.IsPublished
        };
        db.Pages.Add(page);
        if (page.IsPublished)
        {
            AddEditorialAudit(db, user, EditorialAuditAction.Published, "Page", page.Slug);
        }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/pages/by-public-id/{page.PublicId}", ToSummary(page));
    }

    private static async Task<IResult> UpdatePage(
        int id,
        PageRequest request,
        DiscoverJesusDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var validation = ValidatePage(request);
        if (validation is not null)
        {
            return validation;
        }

        var page = await db.Pages.FindAsync([id], cancellationToken);
        if (page is null)
        {
            return Results.NotFound();
        }

        if (await db.Pages.AnyAsync(candidate => candidate.Id != id && candidate.Slug == request.Slug, cancellationToken))
        {
            return Results.Conflict(new { error = "A page with this slug already exists." });
        }

        var wasPublished = page.IsPublished;
        page.Slug = request.Slug;
        page.Title = request.Title;
        page.IsPublished = request.IsPublished;
        if (wasPublished != page.IsPublished)
        {
            AddEditorialAudit(
                db,
                user,
                page.IsPublished ? EditorialAuditAction.Published : EditorialAuditAction.Withdrawn,
                "Page",
                page.PublicId.ToString("D"));
        }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToSummary(page));
    }

    private static async Task<IResult> DeletePage(
        int id,
        DiscoverJesusDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var page = await db.Pages.FindAsync([id], cancellationToken);
        if (page is null)
        {
            return Results.NotFound();
        }

        AddEditorialAudit(
            db,
            user,
            EditorialAuditAction.Deleted,
            "Page",
            page.PublicId.ToString("D"));
        db.Pages.Remove(page);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetComponents(
        int pageId,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Pages.AnyAsync(page => page.Id == pageId, cancellationToken))
        {
            return Results.NotFound();
        }

        var components = await db.Components.AsNoTracking()
            .Where(component => component.PageId == pageId)
            .Include(component => component.Bindings)
            .Include(component => component.Actions)
            .OrderBy(component => component.SortOrder)
            .ToListAsync(cancellationToken);
        return Results.Ok(components.Select(ToResponse));
    }

    private static async Task<IResult> CreateComponent(
        int pageId,
        ComponentRequest request,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        var validation = ValidateComponent(request);
        if (validation is not null)
        {
            return validation;
        }

        if (!await db.Pages.AnyAsync(page => page.Id == pageId, cancellationToken))
        {
            return Results.NotFound();
        }
        if (await db.Components.AnyAsync(
                component => component.PageId == pageId && component.Key == request.Key,
                cancellationToken))
        {
            return Results.Conflict(new { error = "A component with this key already exists on the page." });
        }

        var component = new Component
        {
            PageId = pageId,
            Key = request.Key,
            Type = request.Type,
            PropsJson = request.Props.GetRawText(),
            SortOrder = request.SortOrder
        };
        db.Components.Add(component);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/components/{component.Id}", ToResponse(component));
    }

    private static async Task<IResult> GetComponent(
        int id,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        var component = await db.Components.AsNoTracking()
            .Include(item => item.Bindings)
            .Include(item => item.Actions)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return component is null ? Results.NotFound() : Results.Ok(ToResponse(component));
    }

    private static async Task<IResult> UpdateComponent(
        int id,
        ComponentRequest request,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        var validation = ValidateComponent(request);
        if (validation is not null)
        {
            return validation;
        }

        var component = await db.Components
            .Include(item => item.Bindings)
            .Include(item => item.Actions)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (component is null)
        {
            return Results.NotFound();
        }
        if (await db.Components.AnyAsync(
                item => item.Id != id && item.PageId == component.PageId && item.Key == request.Key,
                cancellationToken))
        {
            return Results.Conflict(new { error = "A component with this key already exists on the page." });
        }

        component.Key = request.Key;
        component.Type = request.Type;
        component.PropsJson = request.Props.GetRawText();
        component.SortOrder = request.SortOrder;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(component));
    }

    private static async Task<IResult> DeleteComponent(
        int id,
        DiscoverJesusDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var component = await db.Components.FindAsync([id], cancellationToken);
        if (component is null)
        {
            return Results.NotFound();
        }

        AddEditorialAudit(
            db,
            user,
            EditorialAuditAction.Deleted,
            "InlineComponent",
            component.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        db.Components.Remove(component);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetBindings(
        int componentId,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Components.AnyAsync(component => component.Id == componentId, cancellationToken))
        {
            return Results.NotFound();
        }

        var bindings = await db.Bindings.AsNoTracking()
            .Where(binding => binding.ComponentId == componentId)
            .OrderBy(binding => binding.Name)
            .ToListAsync(cancellationToken);
        return Results.Ok(bindings.Select(ToResponse));
    }

    private static async Task<IResult> CreateBinding(
        int componentId,
        BindingRequest request,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        var validation = ValidateBinding(request);
        if (validation is not null)
        {
            return validation;
        }

        if (!await db.Components.AnyAsync(component => component.Id == componentId, cancellationToken))
        {
            return Results.NotFound();
        }
        if (await db.Bindings.AnyAsync(
                binding => binding.ComponentId == componentId && binding.Name == request.Name,
                cancellationToken))
        {
            return Results.Conflict(new { error = "A binding with this name already exists on the component." });
        }

        var binding = new Binding
        {
            ComponentId = componentId,
            Name = request.Name,
            Source = request.Source,
            ConfigJson = request.Config.GetRawText()
        };
        db.Bindings.Add(binding);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/bindings/{binding.Id}", ToResponse(binding));
    }

    private static async Task<IResult> GetBinding(
        int id,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        var binding = await db.Bindings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return binding is null ? Results.NotFound() : Results.Ok(ToResponse(binding));
    }

    private static async Task<IResult> UpdateBinding(
        int id,
        BindingRequest request,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        var validation = ValidateBinding(request);
        if (validation is not null)
        {
            return validation;
        }

        var binding = await db.Bindings.FindAsync([id], cancellationToken);
        if (binding is null)
        {
            return Results.NotFound();
        }
        if (await db.Bindings.AnyAsync(
                item => item.Id != id && item.ComponentId == binding.ComponentId && item.Name == request.Name,
                cancellationToken))
        {
            return Results.Conflict(new { error = "A binding with this name already exists on the component." });
        }

        binding.Name = request.Name;
        binding.Source = request.Source;
        binding.ConfigJson = request.Config.GetRawText();
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(binding));
    }

    private static async Task<IResult> DeleteBinding(
        int id,
        DiscoverJesusDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var binding = await db.Bindings.FindAsync([id], cancellationToken);
        if (binding is null)
        {
            return Results.NotFound();
        }

        AddEditorialAudit(
            db,
            user,
            EditorialAuditAction.Deleted,
            "Binding",
            binding.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        db.Bindings.Remove(binding);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetActions(
        int componentId,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Components.AnyAsync(component => component.Id == componentId, cancellationToken))
        {
            return Results.NotFound();
        }

        var actions = await db.Actions.AsNoTracking()
            .Where(action => action.ComponentId == componentId)
            .OrderBy(action => action.Name)
            .ToListAsync(cancellationToken);
        return Results.Ok(actions.Select(ToResponse));
    }

    private static async Task<IResult> CreateAction(
        int componentId,
        ActionRequest request,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        var validation = ValidateAction(request);
        if (validation is not null)
        {
            return validation;
        }

        if (!await db.Components.AnyAsync(component => component.Id == componentId, cancellationToken))
        {
            return Results.NotFound();
        }
        if (await db.Actions.AnyAsync(
                action => action.ComponentId == componentId && action.Name == request.Name,
                cancellationToken))
        {
            return Results.Conflict(new { error = "An action with this name already exists on the component." });
        }

        var action = new ActionDefinition
        {
            ComponentId = componentId,
            Name = request.Name,
            Type = request.Type,
            ConfigJson = request.Config.GetRawText()
        };
        db.Actions.Add(action);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/actions/{action.Id}", ToResponse(action));
    }

    private static async Task<IResult> GetAction(
        int id,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        var action = await db.Actions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return action is null ? Results.NotFound() : Results.Ok(ToResponse(action));
    }

    private static async Task<IResult> UpdateAction(
        int id,
        ActionRequest request,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        var validation = ValidateAction(request);
        if (validation is not null)
        {
            return validation;
        }

        var action = await db.Actions.FindAsync([id], cancellationToken);
        if (action is null)
        {
            return Results.NotFound();
        }
        if (await db.Actions.AnyAsync(
                item => item.Id != id && item.ComponentId == action.ComponentId && item.Name == request.Name,
                cancellationToken))
        {
            return Results.Conflict(new { error = "An action with this name already exists on the component." });
        }

        action.Name = request.Name;
        action.Type = request.Type;
        action.ConfigJson = request.Config.GetRawText();
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(action));
    }

    private static async Task<IResult> DeleteAction(
        int id,
        DiscoverJesusDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var action = await db.Actions.FindAsync([id], cancellationToken);
        if (action is null)
        {
            return Results.NotFound();
        }

        AddEditorialAudit(
            db,
            user,
            EditorialAuditAction.Deleted,
            "Action",
            action.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        db.Actions.Remove(action);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static IResult? ValidatePage(PageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Slug)
            || request.Slug.Length > 200
            || !System.Text.RegularExpressions.Regex.IsMatch(
                request.Slug,
                "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["slug"] = ["Slug must be lowercase letters and numbers separated by single hyphens (max 200 characters)."]
            });
        }
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 300)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["Title is required and must be at most 300 characters."]
            });
        }

        return null;
    }

    private static IResult? ValidateComponent(ComponentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Key) || request.Key.Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["key"] = ["Key is required and must be at most 200 characters."]
            });
        }
        if (string.IsNullOrWhiteSpace(request.Type)
            || request.Type.Length > 100
            || !ContentTypeRules.IsSupportedManagementType(request.Type))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["type"] = ["Type must be a supported component type and at most 100 characters."]
            });
        }
        if (request.Props.ValueKind != JsonValueKind.Object)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["props"] = ["Props must be a JSON object."]
            });
        }

        return null;
    }

    private static IResult? ValidateBinding(BindingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Name is required and must be at most 200 characters."]
            });
        }
        if (string.IsNullOrWhiteSpace(request.Source) || request.Source.Length > 500)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["source"] = ["Source is required and must be at most 500 characters."]
            });
        }
        if (request.Config.ValueKind != JsonValueKind.Object)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["config"] = ["Config must be a JSON object."]
            });
        }

        return null;
    }

    private static IResult? ValidateAction(ActionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Name is required and must be at most 200 characters."]
            });
        }
        if (string.IsNullOrWhiteSpace(request.Type) || request.Type.Length > 100)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["type"] = ["Type is required and must be at most 100 characters."]
            });
        }
        if (request.Config.ValueKind != JsonValueKind.Object)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["config"] = ["Config must be a JSON object."]
            });
        }

        return null;
    }

    private static PageSummary ToSummary(Page page) =>
        new(page.Id, page.PublicId, page.Slug, page.Title, page.IsPublished);

    private static void AddEditorialAudit(
        DiscoverJesusDbContext db,
        ClaimsPrincipal user,
        EditorialAuditAction action,
        string resourceType,
        string resourceId)
    {
        var accountId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        db.EditorialAuditRecords.Add(new EditorialAuditRecord
        {
            AccountId = accountId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            OccurredAt = DateTimeOffset.UtcNow
        });
    }

    private static PageResponse ToResponse(Page page) =>
        new(page.Id, page.PublicId, page.Slug, page.Title, page.IsPublished,
            page.Components.OrderBy(component => component.SortOrder).Select(ToResponse).ToList());

    private static ComponentResponse ToResponse(Component component) =>
        new(component.Id, component.Key, component.Type, ParseJson(component.PropsJson), component.SortOrder,
            component.Bindings.OrderBy(binding => binding.Name).Select(ToResponse).ToList(),
            component.Actions.OrderBy(action => action.Name).Select(ToResponse).ToList());

    private static BindingResponse ToResponse(Binding binding) =>
        new(binding.Id, binding.Name, binding.Source, ParseJson(binding.ConfigJson));

    private static ActionResponse ToResponse(ActionDefinition action) =>
        new(action.Id, action.Name, action.Type, ParseJson(action.ConfigJson));

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
