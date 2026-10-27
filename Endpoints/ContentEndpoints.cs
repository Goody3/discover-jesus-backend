using System.Text.Json;
using DiscoverJesus.Api.Contracts;
using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscoverJesus.Api;

public static class ContentEndpoints
{
    public static IEndpointRouteBuilder MapContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var pages = endpoints.MapGroup("/api/pages").WithTags("Pages");
        pages.MapGet("/", GetPages).WithName("GetPages").WithSummary("List pages")
            .Produces<IReadOnlyList<PageSummary>>();
        pages.MapGet("/{slug}", GetPage).WithName("GetPage")
            .WithSummary("Get a page and its components, bindings, and actions")
            .Produces<PageResponse>()
            .Produces(StatusCodes.Status404NotFound);
        pages.MapPost("/", CreatePage).WithName("CreatePage").WithSummary("Create a page")
            .Produces<PageSummary>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);
        pages.MapPut("/{id:int}", UpdatePage).WithName("UpdatePage").WithSummary("Update a page")
            .Produces<PageSummary>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        pages.MapDelete("/{id:int}", DeletePage).WithName("DeletePage").WithSummary("Delete a page")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        pages.MapGet("/{pageId:int}/components", GetComponents).WithName("GetComponents")
            .WithSummary("List a page's components")
            .Produces<IReadOnlyList<ComponentResponse>>()
            .Produces(StatusCodes.Status404NotFound);
        pages.MapPost("/{pageId:int}/components", CreateComponent).WithName("CreateComponent")
            .WithSummary("Add a component to a page")
            .Produces<ComponentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        endpoints.MapGet("/api/components/{id:int}", GetComponent).WithTags("Components")
            .WithName("GetComponent").WithSummary("Get a component")
            .Produces<ComponentResponse>()
            .Produces(StatusCodes.Status404NotFound);
        endpoints.MapPut("/api/components/{id:int}", UpdateComponent).WithTags("Components")
            .WithName("UpdateComponent").WithSummary("Update a component")
            .Produces<ComponentResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        endpoints.MapDelete("/api/components/{id:int}", DeleteComponent).WithTags("Components")
            .WithName("DeleteComponent").WithSummary("Delete a component")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/api/components/{componentId:int}/bindings", GetBindings)
            .WithTags("Bindings").WithName("GetBindings").WithSummary("List component bindings")
            .Produces<IReadOnlyList<BindingResponse>>()
            .Produces(StatusCodes.Status404NotFound);
        endpoints.MapPost("/api/components/{componentId:int}/bindings", CreateBinding)
            .WithTags("Bindings").WithName("CreateBinding").WithSummary("Add a component binding")
            .Produces<BindingResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        endpoints.MapGet("/api/bindings/{id:int}", GetBinding).WithTags("Bindings")
            .WithName("GetBinding").WithSummary("Get a binding")
            .Produces<BindingResponse>()
            .Produces(StatusCodes.Status404NotFound);
        endpoints.MapPut("/api/bindings/{id:int}", UpdateBinding).WithTags("Bindings")
            .WithName("UpdateBinding").WithSummary("Update a binding")
            .Produces<BindingResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        endpoints.MapDelete("/api/bindings/{id:int}", DeleteBinding).WithTags("Bindings")
            .WithName("DeleteBinding").WithSummary("Delete a binding")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/api/components/{componentId:int}/actions", GetActions)
            .WithTags("Actions").WithName("GetActions").WithSummary("List component actions")
            .Produces<IReadOnlyList<ActionResponse>>()
            .Produces(StatusCodes.Status404NotFound);
        endpoints.MapPost("/api/components/{componentId:int}/actions", CreateAction)
            .WithTags("Actions").WithName("CreateAction").WithSummary("Add a component action")
            .Produces<ActionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        endpoints.MapGet("/api/actions/{id:int}", GetAction).WithTags("Actions")
            .WithName("GetAction").WithSummary("Get an action definition")
            .Produces<ActionResponse>()
            .Produces(StatusCodes.Status404NotFound);
        endpoints.MapPut("/api/actions/{id:int}", UpdateAction).WithTags("Actions")
            .WithName("UpdateAction").WithSummary("Update an action definition")
            .Produces<ActionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        endpoints.MapDelete("/api/actions/{id:int}", DeleteAction).WithTags("Actions")
            .WithName("DeleteAction").WithSummary("Delete an action definition")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetPages(DiscoverJesusDbContext db, CancellationToken cancellationToken)
    {
        var pages = await db.Pages.AsNoTracking()
            .OrderBy(page => page.Slug)
            .Select(page => new PageSummary(page.Id, page.Slug, page.Title, page.IsPublished))
            .ToListAsync(cancellationToken);
        return Results.Ok(pages);
    }

    private static async Task<IResult> GetPage(
        string slug,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        var page = await db.Pages.AsNoTracking()
            .Include(candidate => candidate.Components)
                .ThenInclude(component => component.Bindings)
            .Include(candidate => candidate.Components)
                .ThenInclude(component => component.Actions)
            .SingleOrDefaultAsync(candidate => candidate.Slug == slug, cancellationToken);
        return page is null ? Results.NotFound() : Results.Ok(ToResponse(page));
    }

    private static async Task<IResult> CreatePage(
        PageRequest request,
        DiscoverJesusDbContext db,
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
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/pages/{page.Slug}", ToSummary(page));
    }

    private static async Task<IResult> UpdatePage(
        int id,
        PageRequest request,
        DiscoverJesusDbContext db,
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

        page.Slug = request.Slug;
        page.Title = request.Title;
        page.IsPublished = request.IsPublished;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToSummary(page));
    }

    private static async Task<IResult> DeletePage(
        int id,
        DiscoverJesusDbContext db,
        CancellationToken cancellationToken)
    {
        var page = await db.Pages.FindAsync([id], cancellationToken);
        if (page is null)
        {
            return Results.NotFound();
        }

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
        CancellationToken cancellationToken)
    {
        var component = await db.Components.FindAsync([id], cancellationToken);
        if (component is null)
        {
            return Results.NotFound();
        }

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
        CancellationToken cancellationToken)
    {
        var binding = await db.Bindings.FindAsync([id], cancellationToken);
        if (binding is null)
        {
            return Results.NotFound();
        }

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
        CancellationToken cancellationToken)
    {
        var action = await db.Actions.FindAsync([id], cancellationToken);
        if (action is null)
        {
            return Results.NotFound();
        }

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
        if (string.IsNullOrWhiteSpace(request.Type) || request.Type.Length > 100)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["type"] = ["Type is required and must be at most 100 characters."]
            });
        }
        if (request.Props.ValueKind == JsonValueKind.Undefined)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["props"] = ["Props is required and must be a valid JSON value."]
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
        if (request.Config.ValueKind == JsonValueKind.Undefined)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["config"] = ["Config is required and must be a valid JSON value."]
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
        if (request.Config.ValueKind == JsonValueKind.Undefined)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["config"] = ["Config is required and must be a valid JSON value."]
            });
        }

        return null;
    }

    private static PageSummary ToSummary(Page page) =>
        new(page.Id, page.Slug, page.Title, page.IsPublished);

    private static PageResponse ToResponse(Page page) =>
        new(page.Id, page.Slug, page.Title, page.IsPublished,
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
