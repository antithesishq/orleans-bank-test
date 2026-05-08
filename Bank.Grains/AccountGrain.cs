namespace Bank;

using Orleans.Transactions.Abstractions;

[GenerateSerializer]
public sealed class Balance
{
    [Id(0)]
    public long Units { get; set; }
}

public sealed class AccountGrain : Grain, IAccountGrain
{
    private readonly ITransactionalState<Balance> _balance;

    public AccountGrain([TransactionalState(nameof(_balance))] ITransactionalState<Balance> balance) =>
        _balance = balance ?? throw new ArgumentNullException(nameof(balance));

    public Task<long> DepositReturnBalance(long units) =>
        _balance.PerformUpdate(balance => balance.Units += units);

    // We've removed the precondition that prohibits balance from going negative. This limits the capability to test
    // for Exceptions thrown during the Grain's business logic as part of a Transaction; however, it allows us to
    // simplify the "Bank Test" to be zero-sum and therefore not require initialization of balances.
    public Task<long> WithdrawReturnBalance(long units) =>
        _balance.PerformUpdate(balance => balance.Units -= units);

    public Task<long> GetBalance() =>
        _balance.PerformRead(balance => balance.Units);
}