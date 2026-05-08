namespace Bank;

public enum BankClientMode
{
    Undefined,
    Setup,
    Watchdog,
    Parallel
}

public class BankClientOptions
{
    public BankClientMode Mode { get; set; } = BankClientMode.Undefined;

    public int ParallelTransactionCountMin { get; set; } = 10;
    public int ParallelTransactionCountMax { get; set; } = 1_000;

    public int WatchdogAssertMs { get; set; } = 1_000;
    public int WatchdogTableDumperPort { get; set; } = 54321;
}