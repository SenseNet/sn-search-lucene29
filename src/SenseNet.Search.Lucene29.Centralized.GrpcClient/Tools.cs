using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized.GrpcClient
{
    public static class Tools
    {
        /// <summary>
        /// Serialize an object to an xml string. Uses <see cref="DataContractSerializer"/>.
        /// </summary>
        public static string SerializeSnQuery(SnQuery query)
        {
            var serializer = new DataContractSerializer(query.GetType());

            var xmlWriterSettings = new XmlWriterSettings { Indent = true, IndentChars = "  " };
            var sb = new StringBuilder();
            using (var stringWriter = new StringWriter(sb))
            using (var xmlWriter = XmlWriter.Create(stringWriter, xmlWriterSettings))
                serializer.WriteObject(xmlWriter, query);

            return sb.ToString();
        }
    }
}
