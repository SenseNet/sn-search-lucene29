using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized.Tests
{
    [TestClass]
    public class ServiceRegistrationTests
    {
        [TestMethod]
        public void TestMethod1()
        {
            SearchServiceClient.Instance = null;

            var services = new ServiceCollection()
                .AddLogging()
                .AddLucene29CentralizedSearchEngineWithGrpc(options =>
                {
                    options.ServiceAddress = "amqps://asdsada:adsad-asd435hh456@khd.rmq2.asdsad.com/adsdas";
                })
                .BuildServiceProvider();

            var se = services.GetRequiredService<ISearchEngine>();
            var ie = services.GetRequiredService<IIndexingEngine>();
            var qe = services.GetRequiredService<IQueryEngine>();

            Assert.AreEqual(se.IndexingEngine, ie);
            Assert.AreEqual(se.QueryEngine, qe);
            Assert.IsNotNull(SearchServiceClient.Instance);
        }
    }
}
