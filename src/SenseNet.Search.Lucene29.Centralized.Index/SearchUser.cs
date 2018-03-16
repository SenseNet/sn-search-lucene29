using System.Collections.Generic;
using SenseNet.Security;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    internal class SearchUser : ISecurityUser
    {
        public int Id { get; set; }
        public IEnumerable<int> GetDynamicGroups(int entityId)
        {
            //UNDONE: dynamic groups are not accessible here!
            return new int[0];
        }
    }
}
