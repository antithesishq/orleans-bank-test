namespace Bank;

using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Retry;

internal static class Program
{
    private static async Task Main(params string[] args)
    {
        var retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions() { MaxRetryAttempts = int.MaxValue })
            .Build();

        var hostBuilder = Host.CreateApplicationBuilder(args);

        using var host = hostBuilder
            .UseOrleans(silo =>
            {
                var tableServiceClient = new TableServiceClient(BankOptions.AzuriteConnectionString);

                silo.UseAzureStorageClustering(azure => azure.TableServiceClient = tableServiceClient)
                    .AddAzureTableGrainStorageAsDefault(azure => azure.TableServiceClient = tableServiceClient)
                    .AddAzureTableTransactionalStateStorageAsDefault(azure => azure.TableServiceClient = tableServiceClient)
                    .UseTransactions();
            })
            .Build();

        var cancellationToken = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

        await retry.ExecuteAsync(async ct => await host.RunAsync(ct), cancellationToken);
    }
}