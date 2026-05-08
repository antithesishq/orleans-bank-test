namespace Bank;

using Orleans.Concurrency;

public interface IAccountGrain : IGrainWithIntegerKey
{
    [Transaction(TransactionOption.Join)]
    [ReadOnly]
    Task<long> GetBalance();

    [Transaction(TransactionOption.Join)]
    Task<long> DepositReturnBalance(long units);

    [Transaction(TransactionOption.Join)]
    Task<long> WithdrawReturnBalance(long units);
}