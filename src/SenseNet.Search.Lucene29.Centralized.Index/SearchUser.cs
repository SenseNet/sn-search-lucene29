using System.Collections.Generic;
using SenseNet.Security;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    internal class SearchUser : ISecurityUser
    {
        public int Id { get; set; }
        public int[] DynamicGroups { get; set; }

        public IEnumerable<int> GetDynamicGroups(int entityId)
        {
            return DynamicGroups ?? new int[0];
        }
    }
}
