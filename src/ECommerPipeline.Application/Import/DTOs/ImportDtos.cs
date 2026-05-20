namespace ECommerPipeline.Application.Import.DTOs;

public record ImportResult(
    int TotalRows,
    int SuccessCount,
    int ErrorCount,
    IReadOnlyList<ImportError> Errors);

public record ImportError(int Row, string Message);
