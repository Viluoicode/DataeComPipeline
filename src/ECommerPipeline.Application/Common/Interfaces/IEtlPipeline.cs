namespace ECommerPipeline.Application.Common.Interfaces;

public interface IEtlPipeline
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
