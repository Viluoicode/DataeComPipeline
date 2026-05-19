using ECommerPipeline.Application.Common.Interfaces;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace ECommerPipeline.Infrastructure.Etl;

/// Hangfire entry point. Recurring job binding lives in DependencyInjection.
/// Wraps the ETL pipeline with a Polly retry policy to survive transient DB faults.
/// DisableConcurrentExecution: only one ETL run at a time across all Hangfire workers.
public class EtlJob
{
    private readonly IEtlPipeline _pipeline;
    private readonly ILogger<EtlJob> _logger;
    private readonly ResiliencePipeline _retry;

    public EtlJob(IEtlPipeline pipeline, ILogger<EtlJob> logger)
    {
        _pipeline = pipeline;
        _logger = logger;

        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<SqlException>()
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    _logger.LogWarning(args.Outcome.Exception,
                        "ETL transient failure. Retry {Attempt} in {Delay}s",
                        args.AttemptNumber + 1, args.RetryDelay.TotalSeconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await _retry.ExecuteAsync(async token => await _pipeline.RunAsync(token), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ETL job failed after retries");
            throw;
        }
    }
}
