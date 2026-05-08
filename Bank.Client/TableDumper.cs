namespace Bank;

using Azure.Data.Tables;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class TableDumper
{
    internal static async Task ListenLoop(int port, CancellationToken cancellationToken)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        while (!cancellationToken.IsCancellationRequested)
        {
            var http = await listener.GetContextAsync();

            string tableName = http.Request.Url!.LocalPath.Contains("silo", StringComparison.OrdinalIgnoreCase)
                ? "OrleansSiloInstances"
                : "TransactionalState";

            var service = new TableServiceClient(BankOptions.AzuriteConnectionString);
            var table = service.GetTableClient(tableName);

            var entities = new List<Dictionary<string, object?>>();

            await foreach (var entity in table.QueryAsync<TableEntity>(cancellationToken: cancellationToken))
            {
                entities.Add(
                    entity.Keys.ToDictionary(
                        key => key,
                        key =>
                        {
                            // If the table is storing JSON, we do not want it escaped by JsonSerializer.Serialize.

                            object value = entity[key];

                            if (value is not string s
                                || s.Length == 0
                                || s[0] is not ('{' or '[')
                                || s[^1] is not ('}' or ']'))
                            {
                                return value;
                            }

                            try { return JsonNode.Parse(s); }
                            catch (JsonException) { return value; }
                        }));
            }

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(entities, jsonOptions);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            using var response = http.Response;
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, cancellationToken);
        }
    }
}