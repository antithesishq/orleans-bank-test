namespace Bank;

using System.Collections.Immutable;
using Orleans.Concurrency;

[StatelessWorker]
public sealed class TellerGrain : Grain, ITellerGrain
{
    public async Task<ImmutableArray<long>> GetBalances(int accountCount) =>
    [
        .. await Task.WhenAll(
            Enumerable.Range(0, accountCount)
                .Select(key => GrainFactory.GetGrain<IAccountGrain>(key).GetBalance()))
    ];

    public async Task<(long From, long To)> TransferReturnBalances(IAccountGrain from, IAccountGrain to, long units)
    {
        var balances = await Task.WhenAll(
            from.WithdrawReturnBalance(units),
            to.DepositReturnBalance(units));

        return (balances[0], balances[1]);
    }
}