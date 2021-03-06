using System;
using System.Linq;
using System.Threading.Tasks;
using HexMaster.ShortLink.Core;
using HexMaster.ShortLink.Core.Models.Analytics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;
using HexMaster.ShortLink.Core.Entities;

namespace HexMaster.ShortLink.Resolver.Functions
{
    public static class ResolveShortCodeFunction
    {
        [FunctionName("ResolveShortCodeFunction")]
        public static async Task<IActionResult> ResolveShortCode(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{*path}")]
            HttpRequest req,
            [EventHub(HubNames.ClickEventsHub, Connection = "CloudSettings:EventHubSenderConnectionString")]
            IAsyncCollector<LinkClickedMessage> outputEvents,
            [Table(TableNames.ShortLinks)] CloudTable table,
            string path,
            ILogger log)
        {
            var targetUrl = "https://app.4dn.me/";
            var now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var targetEndpoint = await GetShortLinkEntity(table, path, log);
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

        private static async Task<string> GetShortLinkEntity(CloudTable table, string path, ILogger log)
        {
            log.LogInformation($"Trying to resolve short link with short code {path}");
            var pkQuery = TableQuery.GenerateFilterCondition(PartitionKeys.ShortLinks,
                QueryComparisons.Equal,
                PartitionKeys.ShortLinks);
            var shortCodeQuery =
                TableQuery.GenerateFilterCondition(nameof(ShortLinkEntity.ShortCode),
                    QueryComparisons.Equal, path);
            var expirationQuery = TableQuery.GenerateFilterConditionForDate(
                nameof(ShortLinkEntity.ExpiresOn),
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
