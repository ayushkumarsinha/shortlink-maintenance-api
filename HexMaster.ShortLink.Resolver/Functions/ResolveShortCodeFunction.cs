using System;
using System.Linq;
using System.Threading.Tasks;
using HexMaster.ShortLink.Core.Models.Analytics;
using HexMaster.ShortLink.Data.Entities;
using HexMaster.ShortLink.Messages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;

namespace HexMaster.ShortLink.Resolver.Functions
{
    public static class ResolveShortCodeFunction
    {
        [FunctionName("ResolveShortCodeFunction")]
        public static async Task<IActionResult> ResolveShortCode(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{*path}")]
            HttpRequest req,
            [EventHub(HubNames.ClickEventsHub, Connection = "EventHubConnectionAppSetting")]
            IAsyncCollector<LinkClickedMessage> outputEvents,
            [Table(TableNames.ShortLinks)] CloudTable table,
            string path,
            ILogger log)
        {
            var targetUrl = "https://app.4dn.me/";
            var now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var targetEndpoint = await ShortLinkEntity(table, path, log);
                if (!string.IsNullOrWhiteSpace(targetEndpoint))
                {
                    targetUrl = targetEndpoint;
                    var message = new LinkClickedMessage
                    {
                        Key = path,
                        ClickedAt = now
                    };
                    await outputEvents.AddAsync(message);
                }
            }

            await outputEvents.FlushAsync();
            return new RedirectResult(targetUrl, true);
        }

        private static async Task<string> ShortLinkEntity(CloudTable table, string path, ILogger log)
        {
            log.LogInformation($"Trying to resolve short link with short code {path}");
            var pkQuery = TableQuery.GenerateFilterCondition(nameof(Data.Entities.ShortLinkEntity.PartitionKey),
                QueryComparisons.Equal,
                PartitionKeys.ShortLinks);
            var shortCodeQuery =
                TableQuery.GenerateFilterCondition(nameof(Data.Entities.ShortLinkEntity.ShortCode),
                    QueryComparisons.Equal, path);
            var expirationQuery = TableQuery.GenerateFilterConditionForDate(
                nameof(Data.Entities.ShortLinkEntity.ExpiresOn),
                QueryComparisons.GreaterThanOrEqual, DateTimeOffset.UtcNow);
            var query = new TableQuery<ShortLinkEntity>().Where(
                TableQuery.CombineFilters(expirationQuery, TableOperators.And,
                    TableQuery.CombineFilters(pkQuery, TableOperators.And, shortCodeQuery))
            ).Take(1);
            var ct = new TableContinuationToken();
            var queryResult = await table.ExecuteQuerySegmentedAsync(query, ct);
            var entity = queryResult.Results.FirstOrDefault();
            return entity?.EndpointUrl;
        }
    }
}
