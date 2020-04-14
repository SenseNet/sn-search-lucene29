using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace SenseNet.Search.Lucene29.Centralized.GrpcClient
{
    internal static class Tools
    {
        internal static string Serialize(object obj)
        {
            var serializer = new DataContractSerializer(obj.GetType());

            var xmlWriterSettings = new XmlWriterSettings { Indent = true, IndentChars = "  " };
            var sb = new StringBuilder();
            using (var stringWriter = new StringWriter(sb))
            using (var xmlWriter = XmlWriter.Create(stringWriter, xmlWriterSettings))
                serializer.WriteObject(xmlWriter, obj);

            return sb.ToString();
        }
    }
}
