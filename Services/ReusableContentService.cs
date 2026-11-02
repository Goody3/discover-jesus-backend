using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DiscoverJesus.Api.Contracts;
using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscoverJesus.Api.Services;

public sealed class ReusableContentService(DiscoverJesusDbContext db) : IReusableContentService
{
    private const int MaximumNodeDepth = 64;

    public async Task<IReadOnlyList<ReusableComponentSummary>> GetComponentsAsync(
        CancellationToken cancellationToken) =>
        await db.ReusableComponents.AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new ReusableComponentSummary(
                item.PublicId,
                item.Name,
                item.IsWithdrawn,
                item.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<ReusableContentResult<ReusableComponentDraftResponse>> CreateComponentAsync(
        CreateReusableComponentRequest request,
        CancellationToken cancellationToken)
    {
        var nameError = ValidateComponentName(request.Name);
        if (nameError is not null)
        {
            return Failure<ReusableComponentDraftResponse>(400, "The component name is invalid.", "name", nameError);
        }

        var localeError = ValidateLocale(request.Locale);
        if (localeError is not null)
        {
            return Failure<ReusableComponentDraftResponse>(400, "The locale is invalid.", "locale", localeError);
        }

        var contentValidation = ValidateComponentContent(request.Content);
        if (contentValidation.Errors.Count > 0)
        {
            return ValidationFailure<ReusableComponentDraftResponse>(contentValidation.Errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var normalizedName = NormalizeComponentName(request.Name);
        if (await db.ReusableComponents.AnyAsync(
            item => item.NormalizedName == normalizedName,
            cancellationToken))
        {
            return Conflict<ReusableComponentDraftResponse>(
                "A reusable component with this name already exists.");
        }

        var unavailablePages = await GetUnavailablePagesAsync(
            contentValidation.PageDestinations,
            NormalizeLocale(request.Locale),
            cancellationToken);
        if (unavailablePages.Count > 0)
        {
            return UnavailableDestinations<ReusableComponentDraftResponse>(unavailablePages);
        }

        var now = DateTimeOffset.UtcNow;
        var definition = new ReusableComponentDefinition
        {
            Name = request.Name.Trim(),
            NormalizedName = normalizedName,
            CreatedAt = now
        };
        var draft = new ReusableComponentDraft
        {
            Locale = NormalizeLocale(request.Locale),
            ContentJson = request.Content.GetRawText(),
            UpdatedAt = now,
            Definition = definition
        };
        db.ReusableComponents.Add(definition);
        db.ReusableComponentDrafts.Add(draft);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ReusableContentResult<ReusableComponentDraftResponse>.Success(
            ToResponse(definition.PublicId, definition.Name, draft));
    }

    public async Task<ReusableContentResult<ReusableComponentSummary>> UpdateComponentNameAsync(
        Guid componentId,
        UpdateReusableComponentNameRequest request,
        CancellationToken cancellationToken)
    {
        var nameError = ValidateComponentName(request.Name);
        if (nameError is not null)
        {
            return Failure<ReusableComponentSummary>(400, "The component name is invalid.", "name", nameError);
        }

        var normalizedName = NormalizeComponentName(request.Name);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var definition = await db.ReusableComponents.SingleOrDefaultAsync(
            item => item.PublicId == componentId,
            cancellationToken);
        if (definition is null)
        {
            return NotFound<ReusableComponentSummary>("The reusable component was not found.");
        }

        if (await db.ReusableComponents.AnyAsync(
            item => item.Id != definition.Id && item.NormalizedName == normalizedName,
            cancellationToken))
        {
            return Conflict<ReusableComponentSummary>(
                "A reusable component with this name already exists.");
        }

        definition.Name = request.Name.Trim();
        definition.NormalizedName = normalizedName;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReusableContentResult<ReusableComponentSummary>.Success(
            ToSummary(definition));
    }

    public async Task<ReusableContentResult<ReusableComponentResponse>> GetComponentAsync(
        Guid componentId,
        string locale,
        CancellationToken cancellationToken)
    {
        var localeError = ValidateLocale(locale);
        if (localeError is not null)
        {
            return Failure<ReusableComponentResponse>(400, "The locale is invalid.", "locale", localeError);
        }

        var definition = await db.ReusableComponents.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PublicId == componentId, cancellationToken);
        if (definition is null || definition.IsWithdrawn)
        {
            return NotFound<ReusableComponentResponse>("The reusable component was not found.");
        }

        var revision = await db.ReusableComponentRevisions.AsNoTracking()
            .Where(item => item.DefinitionId == definition.Id
                && item.Locale == NormalizeLocale(locale))
            .OrderByDescending(item => item.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        return revision is null
            ? NotFound<ReusableComponentResponse>("The reusable component has no published revision for this locale.")
            : ReusableContentResult<ReusableComponentResponse>.Success(
                ToResponse(componentId, definition.Name, revision));
    }

    public async Task<ReusableContentResult<ReusableComponentDraftResponse>> GetComponentDraftAsync(
        Guid componentId,
        string locale,
        CancellationToken cancellationToken)
    {
        var localeError = ValidateLocale(locale);
        if (localeError is not null)
        {
            return Failure<ReusableComponentDraftResponse>(400, "The locale is invalid.", "locale", localeError);
        }

        var draft = await db.ReusableComponentDrafts.AsNoTracking()
            .Include(item => item.Definition)
            .SingleOrDefaultAsync(
                item => item.Definition.PublicId == componentId
                    && item.Locale == NormalizeLocale(locale)
                    && !item.Definition.IsWithdrawn,
                cancellationToken);
        return draft is null
            ? NotFound<ReusableComponentDraftResponse>("The reusable component draft was not found.")
            : ReusableContentResult<ReusableComponentDraftResponse>.Success(
                ToResponse(componentId, draft.Definition.Name, draft));
    }

    public async Task<ReusableContentResult<ReusableComponentDraftResponse>> UpdateComponentDraftAsync(
        Guid componentId,
        string locale,
        UpdateReusableComponentDraftRequest request,
        CancellationToken cancellationToken)
    {
        var localeError = ValidateLocale(locale);
        if (localeError is not null)
        {
            return Failure<ReusableComponentDraftResponse>(400, "The locale is invalid.", "locale", localeError);
        }

        var contentValidation = ValidateComponentContent(request.Content);
        if (contentValidation.Errors.Count > 0)
        {
            return ValidationFailure<ReusableComponentDraftResponse>(contentValidation.Errors);
        }

        var normalizedLocale = NormalizeLocale(locale);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var unavailablePages = await GetUnavailablePagesAsync(
            contentValidation.PageDestinations,
            normalizedLocale,
            cancellationToken);
        if (unavailablePages.Count > 0)
        {
            return UnavailableDestinations<ReusableComponentDraftResponse>(unavailablePages);
        }

        var definition = await db.ReusableComponents.SingleOrDefaultAsync(
            item => item.PublicId == componentId,
            cancellationToken);
        if (definition is null || definition.IsWithdrawn)
        {
            return NotFound<ReusableComponentDraftResponse>("The reusable component was not found.");
        }

        var draft = await db.ReusableComponentDrafts.SingleOrDefaultAsync(
            item => item.DefinitionId == definition.Id && item.Locale == normalizedLocale,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (draft is null)
        {
            draft = new ReusableComponentDraft
            {
                DefinitionId = definition.Id,
                Locale = normalizedLocale,
                ContentJson = request.Content.GetRawText(),
                UpdatedAt = now
            };
            db.ReusableComponentDrafts.Add(draft);
        }
        else
        {
            draft.ContentJson = request.Content.GetRawText();
            draft.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReusableContentResult<ReusableComponentDraftResponse>.Success(
            ToResponse(componentId, definition.Name, draft));
    }

    public async Task<ReusableContentResult<ReusableComponentResponse>> PublishComponentAsync(
        Guid componentId,
        string locale,
        CancellationToken cancellationToken)
    {
        var localeError = ValidateLocale(locale);
        if (localeError is not null)
        {
            return Failure<ReusableComponentResponse>(400, "The locale is invalid.", "locale", localeError);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var definition = await db.ReusableComponents.SingleOrDefaultAsync(
            item => item.PublicId == componentId,
            cancellationToken);
        if (definition is null || definition.IsWithdrawn)
        {
            return NotFound<ReusableComponentResponse>("The reusable component was not found.");
        }

        var normalizedLocale = NormalizeLocale(locale);
        var draft = await db.ReusableComponentDrafts.SingleOrDefaultAsync(
            item => item.DefinitionId == definition.Id && item.Locale == normalizedLocale,
            cancellationToken);
        if (draft is null)
        {
            return Conflict<ReusableComponentResponse>("A component draft for this locale is required before publishing.");
        }

        using var contentDocument = JsonDocument.Parse(draft.ContentJson);
        var contentValidation = ValidateComponentContent(contentDocument.RootElement);
        if (contentValidation.Errors.Count > 0)
        {
            return ValidationFailure<ReusableComponentResponse>(contentValidation.Errors);
        }
        var unavailablePages = await GetUnavailablePagesAsync(
            contentValidation.PageDestinations,
            normalizedLocale,
            cancellationToken);
        if (unavailablePages.Count > 0)
        {
            return Unprocessable<ReusableComponentResponse>(
                "The component cannot be published because one or more navigation destinations are unavailable for this locale.",
                new Dictionary<string, string[]>
                {
                    ["pageIds"] = unavailablePages.Select(id => id.ToString()).ToArray()
                });
        }

        var revisionNumber = (await db.ReusableComponentRevisions
            .Where(item => item.DefinitionId == definition.Id && item.Locale == normalizedLocale)
            .MaxAsync(item => (int?)item.RevisionNumber, cancellationToken) ?? 0) + 1;
        var revision = new ReusableComponentRevision
        {
            DefinitionId = definition.Id,
            Locale = normalizedLocale,
            RevisionNumber = revisionNumber,
            ContentJson = draft.ContentJson,
            PublishedAt = DateTimeOffset.UtcNow
        };
        db.ReusableComponentRevisions.Add(revision);
        db.ReusableComponentDrafts.Remove(draft);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ReusableContentResult<ReusableComponentResponse>.Success(
            ToResponse(componentId, definition.Name, revision));
    }

    public async Task<ReusableContentResult<bool>> WithdrawComponentAsync(
        Guid componentId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var definition = await db.ReusableComponents.SingleOrDefaultAsync(
            item => item.PublicId == componentId,
            cancellationToken);
        if (definition is null)
        {
            return NotFound<bool>("The reusable component was not found.");
        }
        if (definition.IsWithdrawn)
        {
            return Conflict<bool>("The reusable component is already withdrawn.");
        }
        if (await HasPageReferencesAsync(componentId, cancellationToken))
        {
            return Conflict<bool>("The component cannot be withdrawn while any saved page draft or revision references it.");
        }

        definition.IsWithdrawn = true;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReusableContentResult<bool>.Success(true);
    }

    public async Task<ReusableContentResult<bool>> DeleteComponentAsync(
        Guid componentId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var definition = await db.ReusableComponents.SingleOrDefaultAsync(
            item => item.PublicId == componentId,
            cancellationToken);
        if (definition is null)
        {
            return NotFound<bool>("The reusable component was not found.");
        }
        if (await HasPageReferencesAsync(componentId, cancellationToken))
        {
            return Conflict<bool>("The component cannot be deleted while any saved page draft or revision references it.");
        }

        db.ReusableComponents.Remove(definition);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReusableContentResult<bool>.Success(true);
    }

    public async Task<ReusableContentResult<PageContentDraftResponse>> GetPageDraftAsync(
        Guid pageId,
        string locale,
        CancellationToken cancellationToken)
    {
        var localeError = ValidateLocale(locale);
        if (localeError is not null)
        {
            return Failure<PageContentDraftResponse>(400, "The locale is invalid.", "locale", localeError);
        }

        var draft = await db.PageContentDrafts.AsNoTracking()
            .Include(item => item.Page)
            .SingleOrDefaultAsync(
                item => item.Page.PublicId == pageId && item.Locale == NormalizeLocale(locale),
                cancellationToken);
        return draft is null
            ? NotFound<PageContentDraftResponse>("The page content draft was not found.")
            : ReusableContentResult<PageContentDraftResponse>.Success(ToResponse(pageId, draft));
    }

    public async Task<ReusableContentResult<PageContentDraftResponse>> StartPageDraftFromPublishedAsync(
        Guid pageId,
        string locale,
        CancellationToken cancellationToken)
    {
        var localeError = ValidateLocale(locale);
        if (localeError is not null)
        {
            return Failure<PageContentDraftResponse>(400, "The locale is invalid.", "locale", localeError);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var page = await db.Pages.SingleOrDefaultAsync(
            item => item.PublicId == pageId,
            cancellationToken);
        if (page is null)
        {
            return NotFound<PageContentDraftResponse>("The page was not found.");
        }

        var normalizedLocale = NormalizeLocale(locale);
        if (await db.PageContentDrafts.AnyAsync(
            item => item.PageId == page.Id && item.Locale == normalizedLocale,
            cancellationToken))
        {
            return Conflict<PageContentDraftResponse>(
                "A page content draft already exists for this locale. Load or save that draft instead.");
        }

        var revision = await db.PageContentRevisions
            .Where(item => item.PageId == page.Id && item.Locale == normalizedLocale)
            .OrderByDescending(item => item.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (revision is null)
        {
            return NotFound<PageContentDraftResponse>(
                "No published page content exists for this locale.");
        }

        var draft = new PageContentDraft
        {
            PageId = page.Id,
            Locale = normalizedLocale,
            SectionsJson = revision.SectionsJson,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var validation = ValidateSections(ParseJson(revision.SectionsJson));
        foreach (var componentId in validation.ComponentReferences)
        {
            draft.ComponentReferences.Add(new PageComponentReference
            {
                PageId = page.Id,
                ComponentPublicId = componentId,
                Draft = draft
            });
        }

        db.PageContentDrafts.Add(draft);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReusableContentResult<PageContentDraftResponse>.Success(ToResponse(pageId, draft));
    }

    public async Task<ReusableContentResult<PageContentDraftResponse>> UpdatePageDraftAsync(
        Guid pageId,
        string locale,
        UpdatePageContentDraftRequest request,
        CancellationToken cancellationToken)
    {
        var localeError = ValidateLocale(locale);
        if (localeError is not null)
        {
            return Failure<PageContentDraftResponse>(400, "The locale is invalid.", "locale", localeError);
        }

        var validation = ValidateSections(request.Sections);
        if (validation.Errors.Count > 0)
        {
            return ValidationFailure<PageContentDraftResponse>(validation.Errors, "sections");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var unavailablePages = await GetUnavailablePagesAsync(
            validation.PageDestinations,
            NormalizeLocale(locale),
            cancellationToken);
        if (unavailablePages.Count > 0)
        {
            return UnavailableDestinations<PageContentDraftResponse>(unavailablePages);
        }

        var page = await db.Pages.SingleOrDefaultAsync(
            item => item.PublicId == pageId,
            cancellationToken);
        if (page is null)
        {
            return NotFound<PageContentDraftResponse>("The page was not found.");
        }

        var normalizedLocale = NormalizeLocale(locale);
        var draft = await db.PageContentDrafts
            .Include(item => item.ComponentReferences)
            .SingleOrDefaultAsync(
                item => item.PageId == page.Id && item.Locale == normalizedLocale,
                cancellationToken);
        if (draft is null)
        {
            draft = new PageContentDraft
            {
                PageId = page.Id,
                Locale = normalizedLocale,
                SectionsJson = request.Sections.GetRawText(),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.PageContentDrafts.Add(draft);
        }
        else
        {
            db.PageComponentReferences.RemoveRange(draft.ComponentReferences);
            draft.SectionsJson = request.Sections.GetRawText();
            draft.UpdatedAt = DateTimeOffset.UtcNow;
        }

        foreach (var componentId in validation.ComponentReferences)
        {
            draft.ComponentReferences.Add(new PageComponentReference
            {
                PageId = page.Id,
                ComponentPublicId = componentId,
                Draft = draft
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReusableContentResult<PageContentDraftResponse>.Success(ToResponse(pageId, draft));
    }

    public async Task<ReusableContentResult<PageContentResponse>> PublishPageAsync(
        Guid pageId,
        string locale,
        CancellationToken cancellationToken)
    {
        var localeError = ValidateLocale(locale);
        if (localeError is not null)
        {
            return Failure<PageContentResponse>(400, "The locale is invalid.", "locale", localeError);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var page = await db.Pages.SingleOrDefaultAsync(
            item => item.PublicId == pageId,
            cancellationToken);
        if (page is null)
        {
            return NotFound<PageContentResponse>("The page was not found.");
        }

        var normalizedLocale = NormalizeLocale(locale);
        var draft = await db.PageContentDrafts
            .Include(item => item.ComponentReferences)
            .SingleOrDefaultAsync(
                item => item.PageId == page.Id && item.Locale == normalizedLocale,
                cancellationToken);
        if (draft is null)
        {
            return Conflict<PageContentResponse>("A page content draft for this locale is required before publishing.");
        }

        using var sectionsDocument = JsonDocument.Parse(draft.SectionsJson);
        var validation = ValidateSections(sectionsDocument.RootElement);
        if (validation.Errors.Count > 0)
        {
            return ValidationFailure<PageContentResponse>(validation.Errors, "sections");
        }

        var unresolved = await GetUnresolvedComponentsAsync(
            validation.ComponentReferences,
            normalizedLocale,
            cancellationToken);
        if (unresolved.Count > 0)
        {
            return Unprocessable<PageContentResponse>(
                "The page cannot be published because one or more component references have no published revision for this locale.",
                new Dictionary<string, string[]>
                {
                    ["componentIds"] = unresolved.Select(id => id.ToString()).ToArray()
                });
        }

        var latestContent = await GetLatestComponentContentAsync(
            validation.ComponentReferences,
            normalizedLocale,
            cancellationToken);
        var pageDestinations = new List<Guid>(validation.PageDestinations);
        foreach (var componentJson in latestContent.Values)
        {
            using var componentDocument = JsonDocument.Parse(componentJson);
            var componentValidation = ValidateComponentContent(componentDocument.RootElement);
            pageDestinations.AddRange(componentValidation.PageDestinations);
        }
        var unavailablePages = await GetUnavailablePagesAsync(
            pageDestinations,
            normalizedLocale,
            cancellationToken);
        if (unavailablePages.Count > 0)
        {
            return Unprocessable<PageContentResponse>(
                "The page cannot be published because one or more navigation destinations are unavailable for this locale.",
                new Dictionary<string, string[]>
                {
                    ["pageIds"] = unavailablePages.Select(id => id.ToString()).ToArray()
                });
        }

        var revisionNumber = (await db.PageContentRevisions
            .Where(item => item.PageId == page.Id && item.Locale == normalizedLocale)
            .MaxAsync(item => (int?)item.RevisionNumber, cancellationToken) ?? 0) + 1;
        var revision = new PageContentRevision
        {
            PageId = page.Id,
            Locale = normalizedLocale,
            RevisionNumber = revisionNumber,
            SectionsJson = draft.SectionsJson,
            PublishedAt = DateTimeOffset.UtcNow
        };
        foreach (var componentId in validation.ComponentReferences)
        {
            revision.ComponentReferences.Add(new PageComponentReference
            {
                PageId = page.Id,
                ComponentPublicId = componentId,
                Revision = revision
            });
        }

        db.PageComponentReferences.RemoveRange(draft.ComponentReferences);
        db.PageContentRevisions.Add(revision);
        db.PageContentDrafts.Remove(draft);
        page.IsPublished = true;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var expanded = await ExpandSectionsAsync(
            revision.SectionsJson,
            normalizedLocale,
            page.PublicId,
            validateNavigation: false,
            cancellationToken);
        return expanded.Error is not null
            ? ReusableContentResult<PageContentResponse>.Failure(
                expanded.Error.StatusCode,
                expanded.Error.Message,
                expanded.Error.ValidationErrors)
            : ReusableContentResult<PageContentResponse>.Success(
                new PageContentResponse(
                    page.PublicId,
                    page.Title,
                    normalizedLocale,
                    revision.RevisionNumber,
                    expanded.Value!));
    }

    public async Task<ReusableContentResult<PageContentResponse>> GetPageAsync(
        Guid pageId,
        string locale,
        CancellationToken cancellationToken)
    {
        var localeError = ValidateLocale(locale);
        if (localeError is not null)
        {
            return Failure<PageContentResponse>(400, "The locale is invalid.", "locale", localeError);
        }

        var normalizedLocale = NormalizeLocale(locale);
        var page = await db.Pages.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PublicId == pageId && item.IsPublished,
                cancellationToken);
        if (page is null)
        {
            return NotFound<PageContentResponse>("The published page was not found.");
        }

        var revision = await db.PageContentRevisions.AsNoTracking()
            .Where(item => item.PageId == page.Id && item.Locale == normalizedLocale)
            .OrderByDescending(item => item.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (revision is null)
        {
            return NotFound<PageContentResponse>("The page has no published content for this locale.");
        }

        var expanded = await ExpandSectionsAsync(
            revision.SectionsJson,
            normalizedLocale,
            page.PublicId,
            validateNavigation: true,
            cancellationToken);
        return expanded.Error is not null
            ? ReusableContentResult<PageContentResponse>.Failure(
                expanded.Error.StatusCode,
                expanded.Error.Message,
                expanded.Error.ValidationErrors)
            : ReusableContentResult<PageContentResponse>.Success(
                new PageContentResponse(
                    page.PublicId,
                    page.Title,
                    normalizedLocale,
                    revision.RevisionNumber,
                    expanded.Value!));
    }

    private async Task<bool> HasPageReferencesAsync(
        Guid componentId,
        CancellationToken cancellationToken) =>
        await db.PageComponentReferences.AnyAsync(
            reference => reference.ComponentPublicId == componentId,
            cancellationToken);

    private async Task<List<Guid>> GetUnresolvedComponentsAsync(
        IReadOnlyCollection<Guid> componentIds,
        string locale,
        CancellationToken cancellationToken)
    {
        if (componentIds.Count == 0)
        {
            return [];
        }

        var normalizedIds = componentIds.Distinct().ToArray();
        var resolvedIds = await db.ReusableComponentRevisions.AsNoTracking()
            .Where(revision => normalizedIds.Contains(revision.Definition.PublicId)
                && revision.Locale == locale
                && !revision.Definition.IsWithdrawn)
            .Select(revision => revision.Definition.PublicId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return normalizedIds.Except(resolvedIds).ToList();
    }

    private async Task<List<Guid>> GetUnavailablePagesAsync(
        IReadOnlyCollection<Guid> pageIds,
        string locale,
        CancellationToken cancellationToken)
    {
        var destinations = pageIds.Distinct().ToArray();
        if (destinations.Length == 0)
        {
            return [];
        }

        var availableIds = await db.Pages.AsNoTracking()
            .Where(page => destinations.Contains(page.PublicId)
                && page.IsPublished
                && page.ContentRevisions.Any(revision => revision.Locale == locale))
            .Select(page => page.PublicId)
            .ToListAsync(cancellationToken);
        return destinations.Except(availableIds).ToList();
    }

    private static ReusableContentResult<T> UnavailableDestinations<T>(
        IReadOnlyCollection<Guid> pageIds) =>
        ReusableContentResult<T>.Failure(
            422,
            "One or more navigation destinations are unavailable for this locale.",
            new Dictionary<string, string[]>
            {
                ["pageIds"] = pageIds.Select(id => id.ToString()).ToArray()
            });

    private async Task<ReusableContentResult<JsonElement>> ExpandSectionsAsync(
        string sectionsJson,
        string locale,
        Guid pageId,
        bool validateNavigation,
        CancellationToken cancellationToken)
    {
        var sections = JsonNode.Parse(sectionsJson);
        if (sections is not JsonArray sectionArray)
        {
            return ReusableContentResult<JsonElement>.Failure(
                500,
                "Stored page sections are not a JSON array.");
        }

        var componentIds = new List<Guid>();
        CollectComponentReferences(sectionArray, componentIds);
        var latestContent = await GetLatestComponentContentAsync(
            componentIds,
            locale,
            cancellationToken);
        var unresolved = componentIds.Distinct()
            .Where(id => !latestContent.ContainsKey(id))
            .ToArray();
        if (unresolved.Length > 0)
        {
            return ReusableContentResult<JsonElement>.Failure(
                404,
                "One or more referenced components are unavailable for this locale.",
                new Dictionary<string, string[]>
                {
                    ["componentIds"] = unresolved.Select(id => id.ToString()).ToArray()
                });
        }

        if (validateNavigation)
        {
                using var sectionsDocument = JsonDocument.Parse(sectionsJson);
                var sectionsValidation = ValidateSections(sectionsDocument.RootElement);
                if (sectionsValidation.Errors.Count > 0)
                {
                    return ReusableContentResult<JsonElement>.Failure(
                        500,
                        "Stored page sections contain invalid navigation data.");
                }

                var pageDestinations = new List<Guid>(sectionsValidation.PageDestinations);
                foreach (var componentJson in latestContent.Values)
                {
                    using var componentDocument = JsonDocument.Parse(componentJson);
                    var componentValidation = ValidateComponentContent(componentDocument.RootElement);
                    if (componentValidation.Errors.Count > 0)
                    {
                        return ReusableContentResult<JsonElement>.Failure(
                            500,
                            "A published reusable component contains invalid navigation data.");
                    }
                    pageDestinations.AddRange(componentValidation.PageDestinations);
                }

                var unavailablePages = await GetUnavailablePagesAsync(
                    pageDestinations,
                    locale,
                    cancellationToken);
                if (unavailablePages.Count > 0)
                {
                    return ReusableContentResult<JsonElement>.Failure(
                        422,
                        "The published page contains navigation destinations that are no longer available for this locale.",
                        new Dictionary<string, string[]>
                        {
                            ["pageIds"] = unavailablePages.Select(id => id.ToString()).ToArray()
                        });
                }
        }

        var expanded = await ExpandNodeAsync(sectionArray, latestContent, 0) as JsonArray;
        if (expanded is null)
        {
            return ReusableContentResult<JsonElement>.Failure(
                500,
                "Expanded page sections are not a JSON array.");
        }

        using var document = JsonDocument.Parse(expanded.ToJsonString());
        return ReusableContentResult<JsonElement>.Success(document.RootElement.Clone());
    }

    private async Task<Dictionary<Guid, string>> GetLatestComponentContentAsync(
        IReadOnlyCollection<Guid> componentIds,
        string locale,
        CancellationToken cancellationToken)
    {
        if (componentIds.Count == 0)
        {
            return [];
        }

        var ids = componentIds.Distinct().ToArray();
        var revisions = await db.ReusableComponentRevisions.AsNoTracking()
            .Where(revision => ids.Contains(revision.Definition.PublicId)
                && revision.Locale == locale
                && !revision.Definition.IsWithdrawn)
            .OrderByDescending(revision => revision.RevisionNumber)
            .Select(revision => new
            {
                ComponentId = revision.Definition.PublicId,
                revision.ContentJson
            })
            .ToListAsync(cancellationToken);
        return revisions
            .GroupBy(revision => revision.ComponentId)
            .ToDictionary(group => group.Key, group => group.First().ContentJson);
    }

    private static async Task<JsonNode?> ExpandNodeAsync(
        JsonNode? node,
        IReadOnlyDictionary<Guid, string> componentContent,
        int depth)
    {
        if (depth > MaximumNodeDepth)
        {
            throw new InvalidOperationException("Stored page content exceeds the maximum expansion depth.");
        }
        if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                var child = array[index];
                var expandedChild = await ExpandNodeAsync(child, componentContent, depth + 1);
                if (!ReferenceEquals(child, expandedChild))
                {
                    array[index] = expandedChild;
                }
            }
            return array;
        }
        if (node is not JsonObject obj)
        {
            return node;
        }

        if (obj["type"] is JsonValue typeValue
            && typeValue.TryGetValue<string>(out var nodeType)
            && nodeType == "componentReference")
        {
            if (obj["componentId"] is not JsonValue idValue
                || !idValue.TryGetValue<string>(out var componentIdText)
                || !Guid.TryParse(componentIdText, out var componentId)
                || !componentContent.TryGetValue(componentId, out var content))
            {
                throw new InvalidOperationException("A published page contains an unresolved component reference.");
            }

            var expanded = JsonNode.Parse(content) as JsonObject
                ?? throw new InvalidOperationException("A published reusable component is not a JSON object.");
            if (obj.TryGetPropertyValue("children", out var childOverride))
            {
                expanded["children"] = childOverride?.DeepClone();
            }
            expanded["sourceComponentId"] = componentId.ToString();
            return await ExpandNodeAsync(expanded, componentContent, depth + 1);
        }

        foreach (var property in obj.ToArray())
        {
            var expandedChild = await ExpandNodeAsync(
                property.Value,
                componentContent,
                depth + 1);
            if (!ReferenceEquals(property.Value, expandedChild))
            {
                obj[property.Key] = expandedChild;
            }
        }
        return obj;
    }

    private static (List<string> Errors, List<Guid> ComponentReferences, List<Guid> PageDestinations)
        ValidateSections(JsonElement sections)
    {
        var errors = new List<string>();
        var componentReferences = new List<Guid>();
        var pageDestinations = new List<Guid>();
        if (sections.ValueKind != JsonValueKind.Array)
        {
            return ([ "Sections must be an array." ], componentReferences, pageDestinations);
        }

        foreach (var section in sections.EnumerateArray())
        {
            ValidateNode(section, "sections", 0, errors, componentReferences, pageDestinations);
        }
        return (errors, componentReferences, pageDestinations);
    }

    private static (List<string> Errors, List<Guid> PageDestinations)
        ValidateComponentContent(JsonElement content)
    {
        var errors = new List<string>();
        var pageDestinations = new List<Guid>();
        if (content.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Component content must be a JSON object.");
            return (errors, pageDestinations);
        }
        ValidateNode(
            content,
            "content",
            0,
            errors,
            [],
            pageDestinations,
            allowComponentReferences: false);
        return (errors, pageDestinations);
    }

    private static void ValidateNode(
        JsonElement node,
        string path,
        int depth,
        List<string> errors,
        List<Guid> componentReferences,
        List<Guid> pageDestinations,
        bool allowComponentReferences = true)
    {
        if (depth > MaximumNodeDepth)
        {
            errors.Add($"{path} exceeds the maximum node depth.");
            return;
        }
        if (node.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path} must be an object.");
            return;
        }

        if (!node.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(typeElement.GetString()))
        {
            errors.Add($"{path}.type is required and must be a non-empty string.");
            return;
        }

        var type = typeElement.GetString();
        if (type == "componentReference")
        {
            if (!allowComponentReferences)
            {
                errors.Add($"{path} cannot contain a reusable component reference.");
                return;
            }

            foreach (var property in node.EnumerateObject())
            {
                if (property.Name is not ("type" or "componentId" or "children"))
                {
                    errors.Add($"{path}.{property.Name} is not allowed on a component reference.");
                }
            }
            if (!node.TryGetProperty("componentId", out var idElement)
                || idElement.ValueKind != JsonValueKind.String
                || !Guid.TryParse(idElement.GetString(), out var componentId))
            {
                errors.Add($"{path}.componentId must be a UUID.");
            }
            else
            {
                componentReferences.Add(componentId);
            }

            if (node.TryGetProperty("children", out var overrideChildren))
            {
                ValidateNodeArray(overrideChildren, $"{path}.children", depth + 1, errors,
                    componentReferences,
                    pageDestinations,
                    allowComponentReferences);
            }
            return;
        }

        if (node.TryGetProperty("sourceComponentId", out _))
        {
            errors.Add($"{path}.sourceComponentId is response-only metadata.");
        }

        foreach (var childPropertyName in new[] { "children", "true", "false" })
        {
            if (node.TryGetProperty(childPropertyName, out var children))
            {
                ValidateNodeArray(children, $"{path}.{childPropertyName}", depth + 1,
                    errors,
                    componentReferences,
                    pageDestinations,
                    allowComponentReferences);
            }
        }
        if (node.TryGetProperty("template", out var template))
        {
            ValidateNode(template, $"{path}.template", depth + 1, errors,
                componentReferences,
                pageDestinations,
                allowComponentReferences);
        }
        if (node.TryGetProperty("action", out var action))
        {
            ValidateAction(action, $"{path}.action", errors, pageDestinations);
        }
    }

    private static void ValidateNodeArray(
        JsonElement value,
        string path,
        int depth,
        List<string> errors,
        List<Guid> componentReferences,
        List<Guid> pageDestinations,
        bool allowComponentReferences = true)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{path} must be an array.");
            return;
        }
        var index = 0;
        foreach (var node in value.EnumerateArray())
        {
            ValidateNode(node, $"{path}[{index++}]", depth, errors,
                componentReferences,
                pageDestinations,
                allowComponentReferences);
        }
    }

    private static void ValidateAction(
        JsonElement action,
        string path,
        List<string> errors,
        List<Guid> pageDestinations,
        int depth = 0)
    {
        if (depth > MaximumNodeDepth)
        {
            errors.Add($"{path} exceeds the maximum action depth.");
            return;
        }
        if (action.ValueKind != JsonValueKind.Object
            || !action.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path} must be an object with a string type.");
            return;
        }

        if (action.TryGetProperty("target", out _))
        {
            errors.Add($"{path}.target is not supported; use a typed destination field.");
        }

        var type = typeElement.GetString();
        if (type is not ("navigate" or "apiCall" or "showModal" or "openExternalUrl"
            or "setLocal" or "getLocal" or "navigateIf" or "sequence" or "animate"))
        {
            errors.Add($"{path}.type is not a supported action type.");
            return;
        }

        switch (type)
        {
            case "navigate":
                ValidatePageId(action, path, errors, pageDestinations);
                RejectOtherDestinations(action, path, errors, "condition", "modalId", "url");
                break;
            case "navigateIf":
                ValidatePageId(action, path, errors, pageDestinations);
                if (!action.TryGetProperty("condition", out var condition)
                    || condition.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(condition.GetString()))
                {
                    errors.Add($"{path}.condition is required for navigateIf.");
                }
                RejectOtherDestinations(action, path, errors, "modalId", "url");
                break;
            case "showModal":
                RequireString(action, "modalId", path, errors);
                RejectOtherDestinations(action, path, errors, "pageId", "url");
                break;
            case "openExternalUrl":
                if (!action.TryGetProperty("url", out var urlElement)
                    || urlElement.ValueKind != JsonValueKind.String
                    || !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps)
                {
                    errors.Add($"{path}.url must be an absolute HTTPS URL.");
                }
                RejectOtherDestinations(action, path, errors, "pageId", "modalId");
                break;
        }

        foreach (var nestedName in new[] { "onSuccess", "onFailure" })
        {
            if (action.TryGetProperty(nestedName, out var nested))
            {
                ValidateAction(nested, $"{path}.{nestedName}", errors, pageDestinations, depth + 1);
            }
        }
        if (action.TryGetProperty("actions", out var actions))
        {
            if (actions.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"{path}.actions must be an array.");
            }
            else
            {
                var index = 0;
                foreach (var nested in actions.EnumerateArray())
                {
                    ValidateAction(nested, $"{path}.actions[{index++}]", errors,
                        pageDestinations, depth + 1);
                }
            }
        }
    }

    private static void ValidatePageId(
        JsonElement action,
        string path,
        List<string> errors,
        List<Guid> pageDestinations)
    {
        if (!action.TryGetProperty("pageId", out var idElement)
            || idElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(idElement.GetString(), out var pageId))
        {
            errors.Add($"{path}.pageId must be a UUID.");
            return;
        }
        pageDestinations.Add(pageId);
    }

    private static void RequireString(
        JsonElement value,
        string propertyName,
        string path,
        List<string> errors)
    {
        if (!value.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            errors.Add($"{path}.{propertyName} is required and must be a non-empty string.");
        }
    }

    private static void RejectOtherDestinations(
        JsonElement action,
        string path,
        List<string> errors,
        params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            if (action.TryGetProperty(fieldName, out _))
            {
                errors.Add($"{path}.{fieldName} is not valid for this action type.");
            }
        }
    }

    private static void CollectComponentReferences(JsonNode? node, List<Guid> results)
    {
        if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                CollectComponentReferences(child, results);
            }
            return;
        }
        if (node is not JsonObject obj)
        {
            return;
        }
        if (obj["type"] is JsonValue typeValue
            && typeValue.TryGetValue<string>(out var nodeType)
            && nodeType == "componentReference"
            && obj["componentId"] is JsonValue idValue
            && idValue.TryGetValue<string>(out var componentIdText)
            && Guid.TryParse(componentIdText, out var componentId))
        {
            results.Add(componentId);
        }
        foreach (var property in obj)
        {
            CollectComponentReferences(property.Value, results);
        }
    }

    private static string? ValidateLocale(string locale) =>
        locale is { Length: <= 35 }
        && Regex.IsMatch(locale, "^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*$")
            ? null
            : "Locale must be a BCP 47-style language tag.";

    private static string? ValidateComponentName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200
            ? null
            : "Name must contain 1 to 200 non-whitespace characters.";

    private static string NormalizeComponentName(string name) =>
        name.Trim().ToUpperInvariant();

    private static string NormalizeLocale(string locale) => locale.ToLowerInvariant();

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static ReusableComponentDraftResponse ToResponse(
        Guid componentId,
        string name,
        ReusableComponentDraft draft) =>
        new(componentId, name, draft.Locale, ParseJson(draft.ContentJson), draft.UpdatedAt);

    private static ReusableComponentResponse ToResponse(
        Guid componentId,
        string name,
        ReusableComponentRevision revision) =>
        new(componentId, name, revision.Locale, revision.RevisionNumber,
            ParseJson(revision.ContentJson), revision.PublishedAt);

    private static ReusableComponentSummary ToSummary(ReusableComponentDefinition definition) =>
        new(definition.PublicId, definition.Name, definition.IsWithdrawn, definition.CreatedAt);

    private static PageContentDraftResponse ToResponse(Guid pageId, PageContentDraft draft) =>
        new(pageId, draft.Locale, ParseJson(draft.SectionsJson), draft.UpdatedAt);

    private static ReusableContentResult<T> NotFound<T>(string message) =>
        ReusableContentResult<T>.Failure(404, message);

    private static ReusableContentResult<T> Conflict<T>(string message) =>
        ReusableContentResult<T>.Failure(409, message);

    private static ReusableContentResult<T> Unprocessable<T>(
        string message,
        IReadOnlyDictionary<string, string[]> errors) =>
        ReusableContentResult<T>.Failure(422, message, errors);

    private static ReusableContentResult<T> ValidationFailure<T>(
        IEnumerable<string> errors,
        string key = "content") =>
        ReusableContentResult<T>.Failure(
            400,
            "The request is invalid.",
            new Dictionary<string, string[]>
            {
                [key] = errors.ToArray()
            });

    private static ReusableContentResult<T> Failure<T>(
        int statusCode,
        string message,
        string key,
        string error) =>
        ReusableContentResult<T>.Failure(
            statusCode,
            message,
            new Dictionary<string, string[]> { [key] = [error] });
}
