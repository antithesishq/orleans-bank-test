namespace Bank;

using System.Collections.Immutable;
using Orleans.Concurrency;

public interface ITellerGrain : IGrainWithIntegerKey
{
    [Transaction(TransactionOption.Create)]
    [ReadOnly]
    Task<ImmutableArray<long>> GetBalances(int accountCount);

    [Transaction(TransactionOption.Create)]
    Task<(long From, long To)> TransferReturnBalances(IAccountGrain from, IAccountGrain to, long units);
}