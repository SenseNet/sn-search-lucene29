using System;
using System.Collections.Generic;
using System.Linq;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Service;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized
{
    public class ServiceQueryContext : IQueryContext
    {
        /// <inheritdoc />
        public QuerySettings Settings { get; }
        /// <inheritdoc />
        public int UserId { get; }
        /// <inheritdoc />
        public IQueryEngine QueryEngine => null; //UNDONE: do we need a local query engine implementation here?
        /// <inheritdoc />
        public IMetaQueryEngine MetaQueryEngine => null;

        /// <summary>
        /// Initializes a new instance of the ServiceQueryContext class.
        /// </summary>
        public ServiceQueryContext(QuerySettings settings, int userId)
        {
            Settings = settings ?? QuerySettings.Default;
            UserId = userId;
        }

        /// <inheritdoc />
        public IPerFieldIndexingInfo GetPerFieldIndexingInfo(string fieldName)
        {
            //return SearchManager.GetPerFieldIndexingInfo(fieldName);

            //UNDONE [QUERY] mock field indexing info, contains a general tolower parser!
            return new ServicePerfieldIndexingInfo();
            //throw new NotImplementedException("Indexing info is not accessible on the service side.");
        }

        /// <summary>
        /// Creates a default context for the content query with the currently logged-in user.
        /// </summary>
        /// <returns></returns>
        public static IQueryContext CreateDefault()
        {
            //UNDONE: [USER] get the user id and settings
            return new ServiceQueryContext(QuerySettings.AdminSettings, 1);
        }
    }

    public class ServicePerfieldIndexingInfo : IPerFieldIndexingInfo
    {
        public IndexFieldAnalyzer Analyzer
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public IFieldIndexHandler IndexFieldHandler
        {
            get => new ServiceFieldIndexHandler();
            set => throw new NotImplementedException();
        }
        public IndexingMode IndexingMode
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }
        public IndexStoringMode IndexStoringMode
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }
        public IndexTermVector TermVectorStoringMode
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }
        public bool IsInIndex => throw new NotImplementedException();

        public Type FieldDataType
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }
    }
    public class ServiceFieldIndexHandler : IFieldIndexHandler
    {
        public IndexValue Parse(string text)
        {
            return new IndexValue(text.ToLowerInvariant());
        }

        public IndexValue ConvertToTermValue(object value)
        {
            throw new NotImplementedException();
        }

        public IndexFieldAnalyzer GetDefaultAnalyzer()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetParsableValues(IIndexableField field)
        {
            throw new NotImplementedException();
        }

        public string GetSortFieldName(string fieldName)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IndexField> GetIndexFields(IIndexableField field, out string textExtract)
        {
            throw new NotImplementedException();
        }

        public IndexValueType IndexFieldType => throw new NotImplementedException();
        public IPerFieldIndexingInfo OwnerIndexingInfo
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }
    }
}
