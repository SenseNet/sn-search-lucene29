using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using SenseNet.Tools;

namespace SenseNet.Search.Lucene29.Centralized.GrpcService
{
    public static class Tools
    {
        /// <summary>
        /// Deserialize an object from an xml string. Uses <see cref="DataContractSerializer"/>.
        /// </summary>
        public static T Deserialize<T>(string srcXml)
        {
            var serializer = new DataContractSerializer(typeof(T));

            object result;
            using (var stringReader = new StringReader(srcXml))
            using (var xmlReader = XmlReader.Create(stringReader))
                result = serializer.ReadObject(xmlReader);

            return (T)result;
        }
    }

    /// <summary>
    /// Empty implementation of the <see cref="IRepositoryBuilder"/> interface.
    /// Lets developers call extension methods on it. Every method throws
    /// NotImplementedException.
    /// </summary>
    internal class EmptyRepositoryBuilder : IRepositoryBuilder
    {
        public T GetProvider<T>() where T : class
        {
            throw new System.NotImplementedException();
        }

        public T GetProvider<T>(string name) where T : class
        {
            throw new System.NotImplementedException();
        }

        public void SetProvider(string providerName, object provider)
        {
            throw new System.NotImplementedException();
        }

        public void SetProvider(object provider)
        {
            throw new System.NotImplementedException();
        }
    }
}
