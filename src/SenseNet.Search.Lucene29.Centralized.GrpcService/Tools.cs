using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace SenseNet.Search.Lucene29.Centralized.GrpcService
{
    internal static class Tools
    {
        internal static T Deserialize<T>(string src)
        {
            var serializer = new DataContractSerializer(typeof(T));

            object result;
            using (var stringReader = new StringReader(src))
            using (var xmlReader = XmlReader.Create(stringReader))
                result = serializer.ReadObject(xmlReader);

            return (T)result;
        }
    }
}
