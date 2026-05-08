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

    private record Context(ResiliencePipeline Retry, BankClientOptions Options, IClusterClient Client,
        CancellationToken CancellationToken);

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

        var context = new Context(retry, options, client, cancellationToken);

        Console.WriteLine(options.Mode);

        try
        {
            await (options.Mode switch
            {
                BankClientMode.Setup => Setup(context),
                BankClientMode.Watchdog => Watchdog(context),
                BankClientMode.Parallel => Parallel(context),

                _ => throw new NotSupportedException(options.Mode.ToString())
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Environment.ExitCode = 0;
        }

        await host.StopAsync(cancellationToken);
    }

    private static async Task Setup(Context context)
    {
        await Assert(context);

        Lifecycle.SetupComplete();

        // We keep Bank.Client alive because otherwise its container would stop and Test Composer would not be able
        // to run the Test Templates and their Commands / Drivers within the container.
        await Task.Delay(-1, context.CancellationToken);
    }

    private static Task Watchdog(Context context)
    {
        return Task.WhenAll(
            AssertLoop(),
            TableDumper.ListenLoop(context.Options.WatchdogTableDumperPort, context.CancellationToken));

        async Task AssertLoop()
        {
            while (!context.CancellationToken.IsCancellationRequested)
            {
                await Assert(context);

                await Task.Delay(context.Options.WatchdogAssertMs, context.CancellationToken);
            }
        }
    }

    private static async Task Parallel(Context context)
    {
        int transactionCount = Antithesis.SDK.Random.SharedFallbackToSystem
            .Next(context.Options.ParallelTransactionCountMin, context.Options.ParallelTransactionCountMax + 1);

        while (transactionCount-- > 0 && !context.CancellationToken.IsCancellationRequested)
        {
            (long From, long To) balances = (0, 0);
            long monotonicBefore = Monotonic;

            await context.Retry.ExecuteAsync(async _ =>
                {
                    balances = await context.Client.GetGrain<ITellerGrain>(0)
                        .TransferReturnBalances(
                            context.Client.GetGrain<IAccountGrain>(0),
                            context.Client.GetGrain<IAccountGrain>(1),
                            1);
                },
                context.CancellationToken);

            await Assert(context, monotonicBefore, balances);
        }
    }

    private static Task Assert(Context context) =>
        Assert(context, 0, []);

    private static Task Assert(Context context, long monotonicBefore, (long From, long To) balances) =>
        Assert(context, monotonicBefore, [balances.From, balances.To]);

    private static async Task Assert(Context context, long monotonicBefore, IReadOnlyList<long> balances)
    {
        if (balances.Count == 0)
        {
            balances = await context.Retry.ExecuteAsync(async _ =>
                    await context.Client.GetGrain<ITellerGrain>(0).GetBalances(AccountsCount),
                context.CancellationToken);
        }

        long monotonicAfter = balances.Select(Math.Abs).Max();
        Monotonic = monotonicAfter;

        long balance0 = balances[0];
        long balance1 = balances[1];
        long totalBalance = balance0 + balance1;

        Console.WriteLine($"{balance0} + {balance1} == {totalBalance}");

        var details = new JsonObject
        {
            [nameof(monotonicBefore)] = monotonicBefore,
            [nameof(monotonicAfter)] = monotonicAfter,
            [nameof(balance0)] = balance0,
            [nameof(balance1)] = balance1
        };

        bool atomicCondition = totalBalance == 0;

        if (context.Options.Mode == BankClientMode.Setup)
            Antithesis.SDK.Assert.Always(atomicCondition, "Atomic - Total Balance is Zero - Setup", details);
        else if (context.Options.Mode == BankClientMode.Watchdog)
            Antithesis.SDK.Assert.Always(atomicCondition, "Atomic - Total Balance is Zero - Watchdog", details);
        else if (context.Options.Mode == BankClientMode.Parallel)
            Antithesis.SDK.Assert.Always(atomicCondition, "Atomic - Total Balance is Zero - Parallel", details);
        else
            throw new NotSupportedException(context.Options.Mode.ToString());

        if (!atomicCondition)
            return;

        bool consistentCondition = context.Options.Mode == BankClientMode.Parallel
            ? monotonicAfter > monotonicBefore
            : monotonicAfter >= monotonicBefore;

        if (context.Options.Mode == BankClientMode.Setup)
            Antithesis.SDK.Assert.Always(consistentCondition, "Consistent - Single Account Balance is Monotonic - Setup", details);
        else if (context.Options.Mode == BankClientMode.Watchdog)
            Antithesis.SDK.Assert.Always(consistentCondition, "Consistent - Single Account Balance is Monotonic - Watchdog", details);
        else if (context.Options.Mode == BankClientMode.Parallel)
            Antithesis.SDK.Assert.Always(consistentCondition, "Consistent - Single Account Balance is Monotonic - Parallel", details);
        else
            throw new NotSupportedException(context.Options.Mode.ToString());
    }

    private static long Monotonic
    {
        get { lock(_monotonicLock) return field; }
        set { lock (_monotonicLock) field = Math.Max(field, value); }
    }

    private static readonly Lock _monotonicLock = new();
}