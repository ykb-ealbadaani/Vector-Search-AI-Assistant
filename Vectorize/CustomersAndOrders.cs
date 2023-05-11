using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using DataCopilot.Vectorize.Models;
using DataCopilot.Vectorize.Services;
using Microsoft.Azure.Cosmos;

namespace DataCopilot.Vectorize
{
    public class CustomersAndOrders
    {

        private readonly OpenAiService _openAI;
        private readonly RedisService _redis;

        public CustomersAndOrders(OpenAiService openAI, RedisService redis)
        {
            _openAI = openAI;
            _redis = redis;
        }

        [FunctionName("CustomersAndOrders")]
        public async Task Run(
            [CosmosDBTrigger(
                databaseName: "database",
                containerName: "customer",
                StartFromBeginning = true,
                Connection = "CosmosDBConnection",
                LeaseContainerName = "leases",
                CreateLeaseContainerIfNotExists = true)]IReadOnlyList<JObject> input,
            [CosmosDB(
                databaseName: "database",
                containerName: "embedding",
                Connection = "CosmosDBConnection")]IAsyncCollector<DocumentVector> output,
            [CosmosDB(
                databaseName: "database",
                containerName: "embedding",
                Connection = "CosmosDBConnection")]CosmosClient cosmosClient,
            ILogger log)
        {


            await _redis.CreateRedisIndex(cosmosClient);

            if (input != null && input.Count > 0)
            {
                log.LogInformation("Generating embeddings for " + input.Count + "Customers and Sales Orders");

                try
                {
                    foreach (dynamic item in input)
                    {

                        if (item.type == "customer")
                        {
                            Customer customer = item.ToObject<Customer>();
                            await GenerateCustomerVectors(customer, output, log);

                        }
                        else
                        if (item.type == "salesOrder")
                        {
                            SalesOrder salesOrder = item.ToObject<SalesOrder>();
                            await GenerateOrderVectors(salesOrder, output, log);

                        }

                    }
                }
                finally
                {

                }
            }
        }


        public async Task GenerateCustomerVectors(Customer customer, IAsyncCollector<DocumentVector> output, ILogger log)
        {

            //Serialize the customer object to send to OpenAI
            string sCustomer = JObject.FromObject(customer).ToString(Newtonsoft.Json.Formatting.None);

            DocumentVector documentVector = new DocumentVector(customer.id, customer.id, "customer");
                          
            try
            {
                //Get the embeddings from OpenAI
                documentVector.vector = await _openAI.GetEmbeddingsAsync(sCustomer);

                //Save to Cosmos DB
                await output.AddAsync(documentVector);

                //Save to Redis Cache
                await _redis.CacheVector(documentVector);

                log.LogInformation("Cached embeddings for customer : " + customer.firstName + " " + customer.lastName);

            }
            catch (Exception x)
            {
                log.LogError("Exception while generating embeddings for [" + customer.firstName + " " + customer.lastName + "]: " + x.Message);
            }

        }

        public async Task GenerateOrderVectors(SalesOrder salesOrder, IAsyncCollector<DocumentVector> output, ILogger log)
        {

            //Serialize the salesOrder to send to OpenAI
            string sSalesOrder = JObject.FromObject(salesOrder).ToString();

            DocumentVector documentVector = new DocumentVector(salesOrder.id, salesOrder.customerId, "customer");
            
            try
            {

                //Get the embeddings from OpenAI
                documentVector.vector = await _openAI.GetEmbeddingsAsync(sSalesOrder);

                //Save to Cosmos DB
                await output.AddAsync(documentVector);

                //Save to Redis Cache
                await _redis.CacheVector(documentVector);

                log.LogInformation("Cached embeddings for Sales Order Id: " + salesOrder.id);

            }
            catch (Exception x)
            {
                log.LogError("Exception while generating embeddings for [" + salesOrder.id + "]: " + x.Message);
            }
        }
    }
}
