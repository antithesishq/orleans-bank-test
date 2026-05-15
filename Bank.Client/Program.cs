namespace Bank;

using Antithesis.SDK;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System.Text.Json.Nodes;

internal static class Program
{
    private const int AccountsCount = 2;

    private static async Task Main(params string[] args)
    {
        // We only support a zero-sum 2 account Bank Test in order to have the simplest code possible (to start)
        // and to create invariants that make triaging simpler.
        if (AccountsCount != 2)
            throw new NotSupportedException();

        var retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions() { MaxRetryAttempts = int.MaxValue })
            .Build();

        var hostBuilder = Host.CreateApplicationBuilder(args);

        hostBuilder.Services
            .AddOptions<BankClientOptions>()
            .BindConfiguration(nameof(BankClientOptions));

        using var host = hostBuilder
            .UseOrleansClient(client =>
                client
                    .UseAzureStorageClustering(azure =>
                        azure.TableServiceClient = new TableServiceClient(BankOptions.AzuriteConnectionString))
                    .UseTransactions())
            .Build();

        var options = host.Services.GetRequiredService<IOptions<BankClientOptions>>().Value;
        var client = host.Services.GetRequiredService<IClusterClient>();
        var cancellationToken = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

        await retry.ExecuteAsync(async ct => await host.StartAsync(ct), cancellationToken);

        Console.WriteLine(options.Mode);

        try
        {
            await (options.Mode switch
            {
                BankClientMode.Setup => Setup(retry, client, cancellationToken),
                BankClientMode.Parallel => Parallel(retry, options, client, cancellationToken),

                _ => throw new NotSupportedException(options.Mode.ToString())
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Environment.ExitCode = 0;
        }

        await host.StopAsync(cancellationToken);
    }

    private static async Task Setup(ResiliencePipeline retry, IClusterClient client,
        CancellationToken cancellationToken)
    {
        // Wait until we can GetBalances for all AccountGrains before emitting SetupComplete.
        await retry.ExecuteAsync(async _ =>
                await client.GetGrain<ITellerGrain>(0).GetBalances(AccountsCount),
            cancellationToken);

        Lifecycle.SetupComplete();

        // We keep Bank.Client alive because otherwise its container would stop and Test Composer would not be able
        // to run the Test Templates and their Commands / Drivers within the container.
        await Task.Delay(-1, cancellationToken);
    }

    private static async Task Parallel(ResiliencePipeline retry, BankClientOptions options, IClusterClient client,
        CancellationToken cancellationToken)
    {
        int transactionCount = Antithesis.SDK.Random.SharedFallbackToSystem
            .Next(options.ParallelTransactionCountMin, options.ParallelTransactionCountMax + 1);

        while (transactionCount-- > 0 && !cancellationToken.IsCancellationRequested)
        {
            (long From, long To) balances = (0, 0);

            await retry.ExecuteAsync(async _ =>
                {
                    balances = await client.GetGrain<ITellerGrain>(0)
                        .TransferReturnBalances(
                            client.GetGrain<IAccountGrain>(0),
                            client.GetGrain<IAccountGrain>(1),
                            1);
                },
                cancellationToken);

            long totalBalance = balances.From + balances.To;

            Console.WriteLine($"{balances.From} + {balances.To} == {totalBalance}");

            Assert.Always(totalBalance == 0, "Atomic - Total Balance is Zero", new JsonObject
            {
                [nameof(balances.From)] = balances.From,
                [nameof(balances.To)] = balances.To
            });
        }
    }
}