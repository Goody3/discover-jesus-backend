using System.Text.Json;
using DiscoverJesus.Api.Contracts;

namespace DiscoverJesus.Api.Services;

public interface IReusableContentService
{
    Task<IReadOnlyList<ReusableComponentSummary>> GetComponentsAsync(
        CancellationToken cancellationToken);

    Task<ReusableContentResult<ReusableComponentDraftResponse>> CreateComponentAsync(
        CreateReusableComponentRequest request,
        CancellationToken cancellationToken);

    Task<ReusableContentResult<ReusableComponentSummary>> UpdateComponentNameAsync(
        Guid componentId,
        UpdateReusableComponentNameRequest request,
        CancellationToken cancellationToken);

    Task<ReusableContentResult<ReusableComponentResponse>> GetComponentAsync(
        Guid componentId,
        string locale,
        CancellationToken cancellationToken);

    Task<ReusableContentResult<ReusableComponentDraftResponse>> GetComponentDraftAsync(
        Guid componentId,
        string locale,
        CancellationToken cancellationToken);

    Task<ReusableContentResult<ReusableComponentDraftResponse>> UpdateComponentDraftAsync(
        Guid componentId,
        string locale,
        UpdateReusableComponentDraftRequest request,
        CancellationToken cancellationToken);

    Task<ReusableContentResult<ReusableComponentResponse>> PublishComponentAsync(
        Guid componentId,
        string locale,
        CancellationToken cancellationToken);

    Task<ReusableContentResult<bool>> WithdrawComponentAsync(
        Guid componentId,
        CancellationToken cancellationToken);

    Task<ReusableContentResult<bool>> DeleteComponentAsync(
        Guid componentId,
        CancellationToken cancellationToken);

    Task<ReusableContentResult<PageContentDraftResponse>> GetPageDraftAsync(
        Guid pageId,
        string locale,
        CancellationToken cancellationToken);

    Task<ReusableContentResult<PageContentDraftResponse>> StartPageDraftFromPublishedAsync(
        Guid pageId,
        string locale,
        CancellationToken cancellationToken);

    Task<ReusableContentResult<PageContentDraftResponse>> UpdatePageDraftAsync(
        Guid pageId,
        string locale,
        UpdatePageContentDraftRequest request,
        CancellationToken cancellationToken);

    Task<ReusableContentResult<PageContentResponse>> PublishPageAsync(
        Guid pageId,
        string locale,
        CancellationToken cancellationToken);

    Task<ReusableContentResult<PageContentResponse>> GetPageAsync(
        Guid pageId,
        string locale,
        CancellationToken cancellationToken);
}
