---

title: "Lucene Search"
source_url: 'https://github.com/SenseNet/sn-search-lucene29/blob/master/docs/lucenesearch.md'
category: Guides
version: v7.0
tags: [lucene, index, indexing, search, query, sn7]
description: sensenet uses the Lucene search engine for indexing and querying content stored in the repository. Every content is indexed, even certain binaries like office documents. The fast search engine and simple query syntax helps you find anything easily.

---

This article is about the specifics of the Lucene search engine component of sensenet. For general concepts and details about indexing please check out the [Field indexing](https://community.sensenet.com/docs/field-indexing/) article.

## Local search engine
This is the default behavior of the Lucene search engine: index files are stored locally in the web folder and **every web folder has its own index**. When an indexing event occurs (e.g. a new document is uploaded) the engine performs the operation locally and *notifies all other web servers* through the messaging module to do the same.

[![NuGet](https://img.shields.io/nuget/v/SenseNet.Search.Lucene29.Local.svg)](https://www.nuget.org/packages/SenseNet.Search.Lucene29.Local)

This solution fits on-premise sensenet installations perfectly as **query operations can be performed in-proc** inside the web application without external service requests, meaning super-fast.

## Centralized search engine
A new approach that eliminates local indexes: there is a single Search Service (a WCF service hosted in a Windows Service on a virtual machine) accessible from all web servers. all query and indexing operations are performed against this search service. The architecture makes indexing truly scalable as web server execute indexing activities only once and they can even help out each other under heavy load. For details, please check out the following article:

- [Search service](search-service.md)

## Configuration
### Code configuration
The easiest way to define the desired search engine is to set it when your application starts (usually this happens using the [RepositoryBuilder](/docs/build-repository) api). The following methods configure either the local or the centralized engine. For other optional configurations please take a look at the app config section below.

Local engine:

```csharp
repositoryBuilder.UseLucene29LocalSearchEngine(indexDirectoryPath);
```

Centralized engine:

```csharp
repositoryBuilder.UseLucene29CentralizedSearchEngine();
```

### Application configuration
There are several aspects of the Lucene search engine that you can configure in the web.config (or any other .Net configuration file where you use the engine).

The following configuration entries can be found or added to the `sensenet/lucene29` section of the config file. If the `lucene29` config section is not defined, please add it to the config sections part:

```xml
<sectionGroup name="sensenet">
  <section name="lucene29" type="System.Configuration.NameValueFileSectionHandler" />
</sectionGroup>
```

#### DefaultTopAndGrowth
Defines the growing number of search results loaded in one batch when executing a query (0 means all). If the first 'page' does not cover the expected number of results, the system will load more and more documents after the criteria has been met.

```xml
<lucene29>
   <add key="DefaultTopAndGrowth" value="100,1000,10000,0" />
</lucene29>
```

####  IndexingEngine
Full class name of the Lucene indexing engine implementation, responsible for writing document data to the index.

Default: local indexing engine.

```xml
<lucene29>
   <add key="IndexingEngine" value="SenseNet.Search.Lucene29.Lucene29LocalIndexingEngine" />
</lucene29>
```

#### QueryEngine
Full class name of the Lucene query engine implementation, responsible for retrieving query results.

Default: local query engine.

```xml
<lucene29>
   <add key="QueryEngine" value="SenseNet.Search.Lucene29.Lucene29LocalQueryEngine" />
</lucene29>
```

#### LuceneMergeFactor
Determines how often segment indices are merged by `addDocument()`. With smaller values, less RAM is used while indexing, and searches on unoptimized indices are faster, but indexing speed is slower. With larger values, more RAM is used during indexing, and while searches on unoptimized indices are slower, indexing is faster. Thus larger values (> 10) are best for batch index creation, and smaller values (< 10) for indices that are interactively maintained.

```xml
<lucene29>
   <add key="LuceneMergeFactor" value="10" />
</lucene29>
```

#### LuceneRAMBufferSizeMB
Determines the amount of RAM that may be used for buffering added documents and deletions before they are flushed to the Directory.

```xml
<lucene29>
   <add key="LuceneRAMBufferSizeMB" value="16.0" />
</lucene29>
```

#### LuceneMaxMergeDocs
Determines the largest segment (measured by document count) that may be merged with other segments. Small values (e.g., less than 10,000) are best for interactive indexing, as this limits the length of pauses while indexing to a few seconds. Larger values are best for batched indexing and speedier searches.

```xml
<lucene29>
   <add key="LuceneMaxMergeDocs" value="[int.MaxValue]" />
</lucene29>
```

#### IndexLockFileWaitForRemovedTimeout
Determines how many seconds the system waits for other processes (or the current writer) to cleanup the lock file during startup or shutdown.

> In a development environment you may set this to a much lower value to speed up cases when you start and stop the site frequently in Visual Studio.

```xml
<lucene29>
   <add key="IndexLockFileWaitForRemovedTimeout" value="120" />
</lucene29>
```

#### LuceneLockDeleteRetryInterval
Determines how many seconds the system waits while tries to delete an existing index lock file during startup.

> In a development environment you may set this to a much lower value to speed up cases when you start and stop the site frequently in Visual Studio.

```xml
<lucene29>
   <add key="LuceneLockDeleteRetryInterval" value="60" />
</lucene29>
```

#### IndexLockFileRemovedNotificationEmail
Email address of an operator where a notification email should be sent when the lock file was not cleaned up by another process during startup. This may indicate that the index was locked by another process.

The sender address (which is necessary for this feature to work) can be configured in the `notification` config section using the `NotificationSender` entry.

```xml
<lucene29>
   <add key="IndexLockFileRemovedNotificationEmail" value="admin@example.com" />
</lucene29>
```