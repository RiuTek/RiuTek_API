using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Common.Interfaces;

public interface IProductImportWorkbookService
{
    Task<ProductImportTemplateDto> GenerateTemplateAsync(
        IReadOnlyList<CategoryLookupDto> categories,
        CancellationToken cancellationToken = default);

    Task<Result<ProductImportParsedWorkbook>> ParseWorkbookAsync(
        Stream fileStream,
        CancellationToken cancellationToken = default);
}
