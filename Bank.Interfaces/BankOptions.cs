namespace Bank;

public static class BankOptions
{
    // These credentials are hardcoded into Azurite and are safe to commit to source control.
    public static string AzuriteConnectionString { get; set; } =
        "DefaultEndpointsProtocol=http;"
        + "AccountName=devstoreaccount1;"
        + "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
        + "BlobEndpoint=http://azurite:10000/devstoreaccount1;"
        + "QueueEndpoint=http://azurite:10001/devstoreaccount1;"
        + "TableEndpoint=http://azurite:10002/devstoreaccount1;";
}