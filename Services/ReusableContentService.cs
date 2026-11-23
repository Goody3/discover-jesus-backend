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
        CancellationToken cancellationToken,
        Guid? actorAccountId = null)
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
        var contentValidation = ValidateComponentContent(
            contentDocument.RootElement,
            requireComplete: true);
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
        if (actorAccountId is Guid actor)
        {
            AddEditorialAudit(actor, EditorialAuditAction.Published, "ReusableComponent", componentId.ToString("D"));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ReusableContentResult<ReusableComponentResponse>.Success(
            ToResponse(componentId, definition.Name, revision));
    }

    public async Task<ReusableContentResult<bool>> WithdrawComponentAsync(
        Guid componentId,
        CancellationToken cancellationToken,
        Guid? actorAccountId = null)
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
        if (actorAccountId is Guid actor)
        {
            AddEditorialAudit(actor, EditorialAuditAction.Withdrawn, "ReusableComponent", componentId.ToString("D"));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReusableContentResult<bool>.Success(true);
    }

    public async Task<ReusableContentResult<bool>> DeleteComponentAsync(
        Guid componentId,
        CancellationToken cancellationToken,
        Guid? actorAccountId = null)
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

        var revisionIds = await db.ReusableComponentRevisions
            .Where(revision => revision.DefinitionId == definition.Id)
            .Select(revision => revision.Id)
            .ToArrayAsync(cancellationToken);
        if (revisionIds.Length > 0)
        {
            var lineages = await db.ReusableComponentRevisionLineages
                .Where(lineage => revisionIds.Contains(lineage.SourceRevisionId)
                    || revisionIds.Contains(lineage.MigratedRevisionId))
                .ToListAsync(cancellationToken);
            db.ReusableComponentRevisionLineages.RemoveRange(lineages);
        }

        if (actorAccountId is Guid actor)
        {
            AddEditorialAudit(actor, EditorialAuditAction.Deleted, "ReusableComponent", componentId.ToString("D"));
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
        CancellationToken cancellationToken,
        Guid? actorAccountId = null)
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
        var validation = ValidateSections(sectionsDocument.RootElement, requireComplete: true);
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
            var componentValidation = ValidateComponentContent(
                componentDocument.RootElement,
                requireComplete: true);
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
        if (actorAccountId is Guid actor)
        {
            AddEditorialAudit(actor, EditorialAuditAction.Published, "PageContent", pageId.ToString("D"));
        }
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

    public async Task<ReusableContentResult<PublishedPageSummary[]>> GetPublishedPagesAsync(
        string locale,
        CancellationToken cancellationToken)
    {
        var localeError = ValidateLocale(locale);
        if (localeError is not null)
        {
            return Failure<PublishedPageSummary[]>(400, "The locale is invalid.", "locale", localeError);
        }

        var normalizedLocale = NormalizeLocale(locale);
        var pages = await db.Pages.AsNoTracking()
            .Where(page => page.IsPublished
                && db.PageContentRevisions.Any(revision =>
                    revision.PageId == page.Id && revision.Locale == normalizedLocale))
            .OrderBy(page => page.Slug)
            .Select(page => new PublishedPageSummary(page.PublicId, page.Slug, page.Title))
            .ToArrayAsync(cancellationToken);

        return ReusableContentResult<PublishedPageSummary[]>.Success(pages);
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
            var sectionsValidation = ValidateSections(
                sectionsDocument.RootElement,
                requireComplete: true);
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
                var componentValidation = ValidateComponentContent(
                    componentDocument.RootElement,
                    requireComplete: true);
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
        ValidateSections(JsonElement sections, bool requireComplete = false)
    {
        var errors = new List<string>();
        var componentReferences = new List<Guid>();
        var pageDestinations = new List<Guid>();
        if (sections.ValueKind != JsonValueKind.Array)
        {
            return (["Sections must be an array."], componentReferences, pageDestinations);
        }

        foreach (var section in sections.EnumerateArray())
        {
            ValidateNode(
                section,
                "sections",
                0,
                errors,
                componentReferences,
                pageDestinations,
                requireComplete: requireComplete);
        }
        return (errors, componentReferences, pageDestinations);
    }

    private static (List<string> Errors, List<Guid> PageDestinations)
        ValidateComponentContent(JsonElement content, bool requireComplete = false)
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
            allowComponentReferences: false,
            requireComplete: requireComplete);
        return (errors, pageDestinations);
    }

    private static void ValidateNode(
        JsonElement node,
        string path,
        int depth,
        List<string> errors,
        List<Guid> componentReferences,
        List<Guid> pageDestinations,
        bool allowComponentReferences = true,
        bool requireComplete = false)
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
            if (!node.TryGetProperty("componentId", out var idElement))
            {
                if (requireComplete)
                {
                    errors.Add($"{path}.componentId is required and must be a UUID.");
                }
            }
            else if (idElement.ValueKind != JsonValueKind.String
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
                    allowComponentReferences,
                    requireComplete);
            }
            return;
        }

        if (type is null || !ContentTypeRules.IsSupportedNodeType(type))
        {
            errors.Add($"{path}.type is not a supported component type.");
            return;
        }

        if (node.TryGetProperty("sourceComponentId", out _))
        {
            errors.Add($"{path}.sourceComponentId is response-only metadata.");
        }

        ValidateNodeShape(node, type, path, errors, requireComplete);

        foreach (var childPropertyName in new[] { "children", "true", "false" })
        {
            if (node.TryGetProperty(childPropertyName, out var children))
            {
                ValidateNodeArray(children, $"{path}.{childPropertyName}", depth + 1,
                    errors,
                    componentReferences,
                    pageDestinations,
                    allowComponentReferences,
                    requireComplete);
            }
        }
        if (node.TryGetProperty("template", out var template))
        {
            ValidateNode(template, $"{path}.template", depth + 1, errors,
                componentReferences,
                pageDestinations,
                allowComponentReferences,
                requireComplete);
        }
        if (node.TryGetProperty("action", out var action))
        {
            ValidateAction(
                action,
                $"{path}.action",
                errors,
                pageDestinations,
                requireComplete: requireComplete);
        }
    }

    private static void ValidateNodeShape(
        JsonElement node,
        string type,
        string path,
        List<string> errors,
        bool requireComplete)
    {
        foreach (var propertyName in new[]
        {
            "value", "label", "reference", "version", "src", "altText", "legacyAttribute",
            "condition", "source", "role"
        })
        {
            ValidateOptionalString(node, propertyName, path, errors);
        }

        if (node.TryGetProperty("headingLevel", out var headingLevel)
            && (headingLevel.ValueKind != JsonValueKind.Number
                || !headingLevel.TryGetInt32(out var level)
                || level is < 1 or > 3))
        {
            errors.Add($"{path}.headingLevel must be 1, 2, or 3.");
        }
        if (node.TryGetProperty("sourcePageId", out var sourcePageId)
            && (sourcePageId.ValueKind != JsonValueKind.String
                || !Guid.TryParse(sourcePageId.GetString(), out _)))
        {
            errors.Add($"{path}.sourcePageId must be a UUID.");
        }

        if (node.TryGetProperty("style", out var style))
        {
            if (style.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{path}.style must be an object.");
            }
            else
            {
                ValidateOptionalString(style, "textSize", $"{path}.style", errors);
                ValidateOptionalString(style, "listStyle", $"{path}.style", errors);
                ValidateOptionalString(style, "alignment", $"{path}.style", errors);
                ValidateOptionalString(style, "fontSize", $"{path}.style", errors);
                if (style.TryGetProperty("textSize", out var textSize)
                    && textSize.ValueKind == JsonValueKind.String
                    && textSize.GetString() is not ("small" or "medium" or "large"))
                {
                    errors.Add($"{path}.style.textSize must be small, medium, or large.");
                }
                if (style.TryGetProperty("listStyle", out var listStyle)
                    && listStyle.ValueKind == JsonValueKind.String
                    && listStyle.GetString() is not ("ordered" or "unordered"))
                {
                    errors.Add($"{path}.style.listStyle must be ordered or unordered.");
                }
            }
        }

        if (node.TryGetProperty("legacySource", out var legacySource))
        {
            if (legacySource.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{path}.legacySource must be an object.");
            }
            else
            {
                RequireProperty(
                    legacySource,
                    "key",
                    JsonValueKind.String,
                    $"{path}.legacySource",
                    errors,
                    required: true);
                if (!legacySource.TryGetProperty("componentId", out var componentId)
                    || componentId.ValueKind != JsonValueKind.Number)
                {
                    errors.Add($"{path}.legacySource.componentId is required and must be a number.");
                }
            }
        }

        if (!node.TryGetProperty("role", out var roleElement)
            || roleElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var role = roleElement.GetString();
        if (role is not ("heading" or "scripture-reference" or "title" or "list"
            or "navigation" or "navigation-item"))
        {
            errors.Add($"{path}.role is not a supported semantic role.");
            return;
        }

        var expectedType = role switch
        {
            "heading" or "scripture-reference" or "title" => "text",
            "list" or "navigation" => "stack",
            "navigation-item" => "button",
            _ => type
        };
        if (type != expectedType)
        {
            errors.Add($"{path}.type must be {expectedType} for role {role}.");
        }

        switch (role)
        {
            case "heading":
                RequireProperty(node, "value", JsonValueKind.String, path, errors, requireComplete);
                RequireProperty(node, "headingLevel", JsonValueKind.Number, path, errors, requireComplete);
                break;
            case "scripture-reference":
                RequireProperty(node, "value", JsonValueKind.String, path, errors, requireComplete);
                RequireProperty(node, "reference", JsonValueKind.String, path, errors, requireComplete);
                RequireProperty(node, "version", JsonValueKind.String, path, errors, requireComplete);
                break;
            case "title":
                RequireProperty(node, "value", JsonValueKind.String, path, errors, requireComplete);
                break;
            case "list":
                RequireProperty(node, "label", JsonValueKind.String, path, errors, requireComplete);
                RequireProperty(node, "children", JsonValueKind.Array, path, errors, requireComplete);
                RequireProperty(node, "style", JsonValueKind.Object, path, errors, requireComplete);
                if (node.TryGetProperty("style", out var listStyle)
                    && listStyle.ValueKind == JsonValueKind.Object)
                {
                    RequireProperty(
                        listStyle,
                        "listStyle",
                        JsonValueKind.String,
                        $"{path}.style",
                        errors,
                        requireComplete);
                }
                if (requireComplete
                    && node.TryGetProperty("children", out var listChildren)
                    && listChildren.ValueKind == JsonValueKind.Array)
                {
                    ValidateListChildren(listChildren, path, errors);
                }
                break;
            case "navigation":
                RequireProperty(node, "children", JsonValueKind.Array, path, errors, requireComplete);
                if (requireComplete
                    && node.TryGetProperty("children", out var navigationChildren)
                    && navigationChildren.ValueKind == JsonValueKind.Array)
                {
                    ValidateNavigationChildren(navigationChildren, path, errors);
                }
                break;
            case "navigation-item":
                RequireProperty(node, "value", JsonValueKind.String, path, errors, requireComplete);
                RequireProperty(node, "action", JsonValueKind.Object, path, errors, requireComplete);
                if (node.TryGetProperty("action", out var navigationAction)
                    && navigationAction.ValueKind == JsonValueKind.Object)
                {
                    if (navigationAction.TryGetProperty("type", out var actionType)
                        && actionType.ValueKind == JsonValueKind.String
                        && actionType.GetString() != "navigate")
                    {
                        errors.Add($"{path}.action.type must be navigate for a navigation-item.");
                    }
                    RequireProperty(
                        navigationAction,
                        "pageId",
                        JsonValueKind.String,
                        $"{path}.action",
                        errors,
                        requireComplete);
                }
                break;
        }
    }

    private static void ValidateListChildren(JsonElement children, string path, List<string> errors)
    {
        var index = 0;
        foreach (var child in children.EnumerateArray())
        {
            var childPath = $"{path}.children[{index++}]";
            if (child.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{childPath} must be an object.");
                continue;
            }
            if (!child.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "text")
            {
                errors.Add($"{childPath}.type must be text.");
            }
            RequireProperty(child, "value", JsonValueKind.String, childPath, errors, required: true);
        }
    }

    private static void ValidateNavigationChildren(
        JsonElement children,
        string path,
        List<string> errors)
    {
        var index = 0;
        foreach (var child in children.EnumerateArray())
        {
            var childPath = $"{path}.children[{index++}]";
            if (child.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{childPath} must be an object.");
                continue;
            }
            if (!child.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "button")
            {
                errors.Add($"{childPath}.type must be button.");
            }
            if (!child.TryGetProperty("role", out var role)
                || role.ValueKind != JsonValueKind.String
                || role.GetString() != "navigation-item")
            {
                errors.Add($"{childPath}.role must be navigation-item.");
            }
            RequireProperty(child, "value", JsonValueKind.String, childPath, errors, required: true);
            RequireProperty(child, "action", JsonValueKind.Object, childPath, errors, required: true);
            if (child.TryGetProperty("action", out var action)
                && action.ValueKind == JsonValueKind.Object)
            {
                if (!action.TryGetProperty("type", out var actionType)
                    || actionType.ValueKind != JsonValueKind.String
                    || actionType.GetString() != "navigate")
                {
                    errors.Add($"{childPath}.action.type must be navigate.");
                }
                RequireProperty(
                    action,
                    "pageId",
                    JsonValueKind.String,
                    $"{childPath}.action",
                    errors,
                    required: true);
            }
        }
    }

    private static void RequireProperty(
        JsonElement value,
        string propertyName,
        JsonValueKind kind,
        string path,
        List<string> errors,
        bool required)
    {
        if (!value.TryGetProperty(propertyName, out var property))
        {
            if (required)
            {
                errors.Add($"{path}.{propertyName} is required.");
            }
            return;
        }
        if (property.ValueKind != kind)
        {
            errors.Add($"{path}.{propertyName} must be {kind.ToString().ToLowerInvariant()}.");
        }
    }

    private static void ValidateNodeArray(
        JsonElement value,
        string path,
        int depth,
        List<string> errors,
        List<Guid> componentReferences,
        List<Guid> pageDestinations,
        bool allowComponentReferences = true,
        bool requireComplete = false)
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
                allowComponentReferences,
                requireComplete);
        }
    }

    private static void ValidateAction(
        JsonElement action,
        string path,
        List<string> errors,
        List<Guid> pageDestinations,
        int depth = 0,
        bool requireComplete = false)
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

        ValidateOptionalString(action, "pageId", path, errors);
        ValidateOptionalString(action, "modalId", path, errors);
        ValidateOptionalString(action, "url", path, errors);
        ValidateOptionalString(action, "condition", path, errors);
        ValidateOptionalString(action, "endpoint", path, errors);
        ValidateOptionalString(action, "method", path, errors);
        ValidateOptionalString(action, "animation", path, errors);
        ValidateOptionalObject(action, "params", path, errors);
        ValidateOptionalObject(action, "body", path, errors);

        if (action.TryGetProperty("method", out var method)
            && method.ValueKind == JsonValueKind.String
            && method.GetString() is not ("GET" or "POST" or "PUT" or "DELETE"))
        {
            errors.Add($"{path}.method must be GET, POST, PUT, or DELETE.");
        }
        if (action.TryGetProperty("animation", out var animation)
            && animation.ValueKind == JsonValueKind.String
            && animation.GetString() is not ("fadeIn" or "fadeOut" or "slideUp" or "slideDown"))
        {
            errors.Add($"{path}.animation is not a supported animation.");
        }

        switch (type)
        {
            case "navigate":
                if (requireComplete || action.TryGetProperty("pageId", out _))
                {
                    ValidatePageId(action, path, errors, pageDestinations);
                }
                RejectOtherDestinations(action, path, errors, "condition", "modalId", "url");
                break;
            case "navigateIf":
                if (requireComplete || action.TryGetProperty("pageId", out _))
                {
                    ValidatePageId(action, path, errors, pageDestinations);
                }
                ValidateRequiredString(action, "condition", path, errors, requireComplete);
                RejectOtherDestinations(action, path, errors, "modalId", "url");
                break;
            case "showModal":
                ValidateRequiredString(action, "modalId", path, errors, requireComplete);
                RejectOtherDestinations(action, path, errors, "pageId", "url");
                break;
            case "openExternalUrl":
                ValidateRequiredString(action, "url", path, errors, requireComplete);
                if (action.TryGetProperty("url", out var urlElement)
                    && urlElement.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(urlElement.GetString())
                    && (!Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var uri)
                        || uri.Scheme != Uri.UriSchemeHttps))
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
                ValidateAction(
                    nested,
                    $"{path}.{nestedName}",
                    errors,
                    pageDestinations,
                    depth + 1,
                    requireComplete);
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
                        pageDestinations, depth + 1, requireComplete);
                }
            }
        }
    }

    private static void ValidateOptionalString(
        JsonElement value,
        string propertyName,
        string path,
        List<string> errors)
    {
        if (value.TryGetProperty(propertyName, out var property)
            && property.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path}.{propertyName} must be a string.");
        }
    }

    private static void ValidateOptionalObject(
        JsonElement value,
        string propertyName,
        string path,
        List<string> errors)
    {
        if (value.TryGetProperty(propertyName, out var property)
            && property.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}.{propertyName} must be an object.");
        }
    }

    private static void ValidateRequiredString(
        JsonElement value,
        string propertyName,
        string path,
        List<string> errors,
        bool requireComplete)
    {
        if (!value.TryGetProperty(propertyName, out var property))
        {
            if (requireComplete)
            {
                errors.Add($"{path}.{propertyName} is required.");
            }
            return;
        }
        if (property.ValueKind == JsonValueKind.String
            && requireComplete
            && string.IsNullOrWhiteSpace(property.GetString()))
        {
            errors.Add($"{path}.{propertyName} must not be empty when publishing.");
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
        !string.IsNullOrWhiteSpace(name) && name.Length <= 200
            ? null
            : "Name must contain 1 to 200 characters and cannot be whitespace.";

    private static string NormalizeComponentName(string name) =>
        name.Trim().ToUpperInvariant();

    private static string NormalizeLocale(string locale) => locale.ToLowerInvariant();

    private void AddEditorialAudit(
        Guid accountId,
        EditorialAuditAction action,
        string resourceType,
        string resourceId) =>
        db.EditorialAuditRecords.Add(new EditorialAuditRecord
        {
            AccountId = accountId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            OccurredAt = DateTimeOffset.UtcNow
        });

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
