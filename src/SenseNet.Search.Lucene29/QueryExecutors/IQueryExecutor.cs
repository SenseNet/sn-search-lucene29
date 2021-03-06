﻿using System.Collections.Generic;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.QueryExecutors
{
    internal interface IQueryExecutor
    {
        IPermissionFilter PermissionChecker { get; }
        LucQuery LucQuery { get; }

        void Initialize(LucQuery lucQuery, IPermissionFilter permisionChecker);

        string QueryString { get; }

        int TotalCount { get; }
        IEnumerable<LucObject> Execute();
    }
}
