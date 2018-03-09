using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace SenseNet.Search.Lucene29.Centralized
{
    //UNDONE: consider merging these two back types to the generic solution
    [DataContract]
    public class ServiceQueryResultInt
    {
        [DataMember]
        public int[] Hits { get; set; }
        [DataMember]
        public int TotalCount { get; set; }

        public ServiceQueryResultInt(IEnumerable<int> hits, int totalCount)
        {
            Hits = hits.ToArray();
            TotalCount = totalCount;
        }
    }
    [DataContract]
    public class ServiceQueryResultString
    {
        [DataMember]
        public string[] Hits { get; set; }
        [DataMember]
        public int TotalCount { get; set; }

        public ServiceQueryResultString(IEnumerable<string> hits, int totalCount)
        {
            Hits = hits.ToArray();
            TotalCount = totalCount;
        }
    }
}
