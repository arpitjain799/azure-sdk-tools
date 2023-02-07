using System;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Azure.Sdk.Tools.PipelineOwnersExtractor
{
    public class Program
    {
        private static readonly JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.Indented, Converters = { new StringEnumConverter() } };

        public static async Task Main(string[] args)
        {
            Console.WriteLine("Initializing PipelineOwnersExtractor");

            await DumpMeInfoAsync(new ManagedIdentityCredential(null, new TokenCredentialOptions{ Retry = { MaxRetries = 2, Delay = TimeSpan.FromSeconds(1), NetworkTimeout = TimeSpan.FromSeconds(3)} }));
            await DumpMeInfoAsync(new AzureCliCredential());
            await DumpMeInfoAsync(new AzurePowerShellCredential());
            await DumpMeInfoAsync(new DefaultAzureCredential());
        }

        public static async Task DumpMeInfoAsync(TokenCredential credential)
        {
            Console.WriteLine();
            Console.WriteLine(credential.GetType().Name);

            try
            {
                string[] scopes = { "https://graph.microsoft.com/.default" };

                var graphClient = new GraphServiceClient(credential, scopes);

                var user = await graphClient.Me.Request().GetAsync();

                Console.WriteLine(JsonConvert.SerializeObject(user, jsonSerializerSettings));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            Console.WriteLine();
        }
    }
}
