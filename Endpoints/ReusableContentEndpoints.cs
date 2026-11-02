using DiscoverJesus.Api.Contracts;
using DiscoverJesus.Api.Services;

namespace DiscoverJesus.Api;

public static class ReusableContentEndpoints
{
    public static IEndpointRouteBuilder MapReusableContentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var reusableComponents = endpoints.MapGroup("/api/reusable-components")
            .WithTags("Reusable components");
        reusableComponents.MapGet("", GetComponents)
            .WithName("GetReusableComponents")
            .WithSummary("List reusable components by friendly name")
            .Produces<IReadOnlyList<ReusableComponentSummary>>();
        reusableComponents.MapPost("", CreateComponent)
            .WithName("CreateReusableComponent")
            .WithSummary("Create a named reusable component and locale draft")
            .Produces<ReusableComponentDraftResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);
        reusableComponents.MapPut("/{componentId:guid}", UpdateComponentName)
            .WithName("UpdateReusableComponentName")
            .WithSummary("Change a reusable component's friendly name")
            .Produces<ReusableComponentSummary>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        reusableComponents.MapGet("/{componentId:guid}", GetComponent)
            .WithName("GetReusableComponent")
            .WithSummary("Get the latest published reusable component revision for a locale")
            .Produces<ReusableComponentResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);
        reusableComponents.MapGet("/{componentId:guid}/draft", GetComponentDraft)
            .WithName("GetReusableComponentDraft")
            .WithSummary("Get a reusable component draft for a locale")
            .Produces<ReusableComponentDraftResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);
        reusableComponents.MapPut("/{componentId:guid}/draft", UpdateComponentDraft)
            .WithName("UpdateReusableComponentDraft")
            .WithSummary("Create or replace a reusable component locale draft")
            .Produces<ReusableComponentDraftResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);
        reusableComponents.MapPost("/{componentId:guid}/publish", PublishComponent)
            .WithName("PublishReusableComponent")
            .WithSummary("Publish a validated locale draft as an immutable revision")
            .Produces<ReusableComponentResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);
        reusableComponents.MapPost("/{componentId:guid}/withdraw", WithdrawComponent)
            .WithName("WithdrawReusableComponent")
            .WithSummary("Withdraw a reusable component when no page versions reference it")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        reusableComponents.MapDelete("/{componentId:guid}", DeleteComponent)
            .WithName("DeleteReusableComponent")
            .WithSummary("Delete a reusable component when no page versions reference it")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        var pageContent = endpoints.MapGroup("/api/content/pages")
            .WithTags("Page content");
        pageContent.MapGet("/{pageId:guid}", GetPage)
            .WithName("GetPageContent")
            .WithSummary("Get a published page with reusable components expanded")
            .Produces<PageContentResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);
        pageContent.MapGet("/{pageId:guid}/draft", GetPageDraft)
            .WithName("GetPageContentDraft")
            .WithSummary("Get a page content draft for a locale")
            .Produces<PageContentDraftResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);
        pageContent.MapPost("/{pageId:guid}/draft/from-published", StartPageDraftFromPublished)
            .WithName("StartPageDraftFromPublished")
            .WithSummary("Start a page draft by copying its latest published sections")
            .Produces<PageContentDraftResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        pageContent.MapPut("/{pageId:guid}/draft", UpdatePageDraft)
            .WithName("UpdatePageContentDraft")
            .WithSummary("Create or replace a page content draft")
            .Produces<PageContentDraftResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);
        pageContent.MapPost("/{pageId:guid}/publish", PublishPage)
            .WithName("PublishPageContent")
            .WithSummary("Validate references and publish a page content revision")
            .Produces<PageContentResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> GetComponents(
        IReusableContentService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetComponentsAsync(cancellationToken));

    private static async Task<IResult> CreateComponent(
        CreateReusableComponentRequest request,
        IReusableContentService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateComponentAsync(request, cancellationToken);
        return result.Succeeded
            ? Results.Created(
                $"/api/reusable-components/{result.Value!.ComponentId}/draft?locale={Uri.EscapeDataString(result.Value.Locale)}",
                result.Value)
            : ToResult(result);
    }

    private static async Task<IResult> UpdateComponentName(
        Guid componentId,
        UpdateReusableComponentNameRequest request,
        IReusableContentService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.UpdateComponentNameAsync(
            componentId,
            request,
            cancellationToken));

    private static async Task<IResult> GetComponent(
        Guid componentId,
        string locale,
        IReusableContentService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.GetComponentAsync(componentId, locale, cancellationToken));

    private static async Task<IResult> GetComponentDraft(
        Guid componentId,
        string locale,
        IReusableContentService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.GetComponentDraftAsync(componentId, locale, cancellationToken));

    private static async Task<IResult> UpdateComponentDraft(
        Guid componentId,
        string locale,
        UpdateReusableComponentDraftRequest request,
        IReusableContentService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.UpdateComponentDraftAsync(
            componentId,
            locale,
            request,
            cancellationToken));

    private static async Task<IResult> PublishComponent(
        Guid componentId,
        string locale,
        IReusableContentService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.PublishComponentAsync(componentId, locale, cancellationToken));

    private static async Task<IResult> WithdrawComponent(
        Guid componentId,
        IReusableContentService service,
        CancellationToken cancellationToken)
    {
        var result = await service.WithdrawComponentAsync(componentId, cancellationToken);
        return result.Succeeded ? Results.NoContent() : ToResult(result);
    }

    private static async Task<IResult> DeleteComponent(
        Guid componentId,
        IReusableContentService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteComponentAsync(componentId, cancellationToken);
        return result.Succeeded ? Results.NoContent() : ToResult(result);
    }

    private static async Task<IResult> GetPage(
        Guid pageId,
        string locale,
        IReusableContentService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.GetPageAsync(pageId, locale, cancellationToken));

    private static async Task<IResult> GetPageDraft(
        Guid pageId,
        string locale,
        IReusableContentService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.GetPageDraftAsync(pageId, locale, cancellationToken));

    private static async Task<IResult> StartPageDraftFromPublished(
        Guid pageId,
        string locale,
        IReusableContentService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.StartPageDraftFromPublishedAsync(pageId, locale, cancellationToken));

    private static async Task<IResult> UpdatePageDraft(
        Guid pageId,
        string locale,
        UpdatePageContentDraftRequest request,
        IReusableContentService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.UpdatePageDraftAsync(pageId, locale, request, cancellationToken));

    private static async Task<IResult> PublishPage(
        Guid pageId,
        string locale,
        IReusableContentService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.PublishPageAsync(pageId, locale, cancellationToken));

    private static IResult ToResult<T>(ReusableContentResult<T> result)
    {
        if (result.Succeeded)
        {
            return Results.Ok(result.Value);
        }

        var error = result.Error!;
        if (error.StatusCode == StatusCodes.Status400BadRequest
            && error.ValidationErrors is not null)
        {
            return Results.ValidationProblem(
                error.ValidationErrors.ToDictionary(entry => entry.Key, entry => entry.Value),
                title: error.Message);
        }

        if (error.StatusCode == StatusCodes.Status422UnprocessableEntity)
        {
            return Results.Problem(
                title: error.Message,
                statusCode: error.StatusCode,
                extensions: error.ValidationErrors is null
                    ? null
                    : new Dictionary<string, object?> { ["errors"] = error.ValidationErrors });
        }

        return Results.Problem(
            title: error.Message,
            statusCode: error.StatusCode);
    }
}
