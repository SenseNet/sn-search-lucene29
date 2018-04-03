---

title: "Lucene Search Service"
source_url: 'https://github.com/SenseNet/sn-search-lucene29/blob/master/docs/search-service.md'
category: Guides
version: v7.0
tags: [lucene, index, indexing, search, query, service, sn7]
description: The Lucene search engine needs a physical file storage for its index files. In a cloud environment it is not possible to store these index files inside the web folder. This is why we created a central search service, separate from the web applications.

---

In this article you'll learn about the architecture of the **Search Service** in sensenet and how can you deploy and configure it.

As Lucene needs to store its index files in the file system, we have to have a physical location for index files. This is why in on-premise environments every web server has its own local index. The drawback of this architecture is that index folders need to be kept in sync and index operations need to be performed in every web application.

> **History**: at some point in the past we had a solution for all web applications working with a shared index folder that was stored on a shared drive accessible for every web server, but that came with many problems, file sharing errors only one of them.

In a **cloud environment** adding or removing web servers should be easy and fast (and mainly: **automatic**) we had to come up with a solution where the index is held in a central place.

## Central Search Service
The Search Service is the main entry point for *indexing and querying* operations. It is a **Windows Service** that can be installed on a virtual machine. The single index folder is there, there is no local index on web servers.

All query operations are executed against this central index. This way there is no need for local files on the web server, which is a huge step towards auto-scaling.

### Indexing
There is a huge advantage over the local indexing architecture: if the index is in a central place, **indexing operaitons need to be performed only once**, on one of the web servers (it does not even have to be the server where the operation was originated, it can be any one of them!). Other web servers will see the changes immediately, because they use the same service for querying. 

This makes indexing faster and **truly scalable**. Adding new web servers to the system will help with the load as indexing operations can be performed on any of the servers. There is an advanced indexing activity queue that makes sure (with the help of a db row locking algorithm) that activities are loaded and executed only once and in the appropriate order. Independent activities (like indexing two documents) are executed in parallel of course.

> **Backup**: it is important to note that in the current implementation you have to **shut down the service** if you want to make a backup of the index to make sure that there are no changes in index files during the backup operation. This will make all the web sites unavailable during backup.

### Querying
All queries are executed against the central index. To make this as fast as possible, we use the **TCP protocol** for communication. Query results are basically content ids (integers) so the payload is relatively small.

### Security
As sensenet has a sophisticated and smart permission system we have to make sure that the search service returns query results appropriate for the current user.

This is why the search service needs to have access to the same permission values as web servers - and follow the dynamic changes too that are made by administrators on the live site. To achieve this you have to allow **the service access the same security database** as all web servers. In most cases this is the content repository itself, but it could be a totally separate db too.

See the *Configuration* section below on how can you configure the connection string and communication channels to make this work.

## Deployment
### Installing the service
As the Search Service is a Windows Service, it needs to be installed on the virtual machine you plan to use as the service server. Please follow these steps to set up the service:

- extract the Search Service zip package you downloaded from the sensenet Lucene [release page](https://github.com/SenseNet/sn-search-lucene29/releases) and copy the service folder to a virtual machine (there is no restriction on the place, you can put it anywhere)
- install the service using the built-in Windows command line tool:

`installutil SenseNet.Search.Lucene29.Centralized.Service.exe`

> If the *installutil* tool is not found automatically, you'll find it the following folder: 
>
> *C:\Windows\Microsoft.NET\Framework64\v4.0.30319*

Make sure you set up the service to start with a **user identity** that has access to the sensenet security database.

### Installing the NuGet package
For the web server side you have to install the following NuGet package in Visual Studio. This will add the necessary configuration lines to your web.config, but you will need to fill them properly - see the *Configuration* section below for details.

[![NuGet](https://img.shields.io/nuget/v/SenseNet.Search.Lucene29.Centralized.svg)](https://www.nuget.org/packages/SenseNet.Search.Lucene29.Centralized)

## Configuration
The service has a configuration file (*SenseNet.Search.Lucene29.Centralized.Service.exe.config*) that you should set up before starting the service.

### Service binding and endpoint
The centralized search service is a Windows Service that hosts a **WCF service**. This gives you a very flexible configuration that you can adjust to your environment. As a minimum requirement you have to configure the following value:

- the **base address** (including the port) you want to use to access your search service

```xml
<baseAddresses>
    <add baseAddress="net.tcp://example.com:50123/SenseNetServiceModel/service/SearchService"/>
</baseAddresses>
```

#### Web.config on all web servers
Please make sure that the indexing and query engines are properly configured (the nuget package should do this for you when you install it in Visual Studio):

```xml
<sensenet>
   <lucene29>
      <add key="IndexingEngine" value="SenseNet.Search.Lucene29.Lucene29CentralizedIndexingEngine" />
      <add key="QueryEngine" value="SenseNet.Search.Lucene29.Lucene29CentralizedQueryEngine" />
   </lucene29>
</sensenet>
```

You have to configure the same **address** in the *system.ServiceModel* section as on the service side. The whole section should be added by NuGet by now. No need to change anything else, you may fine-tune the WCF client configuration later.

```xml
<system.serviceModel>
   <serviceHostingEnvironment aspNetCompatibilityEnabled="true" multipleSiteBindingsEnabled="true" />
   <bindings>
      <netTcpBinding>
         <binding name="NetTcpBinding_ISearchServiceContract" />
      </netTcpBinding>
   </bindings>
   <client>
      <endpoint address="net.tcp://example.com:50123/SenseNetServiceModel/service/SearchService"
                binding="netTcpBinding"
                bindingConfiguration="NetTcpBinding_ISearchServiceContract"
                contract="SenseNet.Search.Lucene29.Centralized.Common.ISearchServiceContract"
                name="NetTcpBinding_ISearchServiceContract">
      </endpoint>
   </client>
</system.serviceModel>
```

### Security db and messaging
Please provide a connection string to your database (by default this is the content repository db) where you have the security tables.

```xml
<connectionStrings>
    <add name="SecurityStorage" connectionString="..." providerName="System.Data.SqlClient" />
</connectionStrings>
```

Currently security messages arrive through *MSMQ*. The search service acts as a message receiver, therefore it needs its **own security messaging channel**. This channel also needs to be added to the configuration of *all the web servers* so that they can send update messages to the service.

```xml
<appSettings>
    <add key="SecurityMsmqChannelQueueName" value="..." />
</appSettings>
```

### Providers
It is possible to configure the security messaging and data providers the same way as in the main web application - although the defaults (MSMQ and Entity Framework) should be fine.

```xml
<sensenet>
    <providers>   
        <add key="SecurityDataProvider" value="..." />
        <add key="SecurityMessageProvider" value="..." />
    </providers>
</sensenet>
```

### Indexing
The *sensenet/lucene29* section may contain the same values as on the web server (except of course the indexing and query engine lines that does not make *sense* here). To see a complete list, please take a look at the [Lucene search](lucenesearch.md) article.

### Tracing
If you need to see what is happening inside the service in real time, you can switch on the usual tracing categories in the *sensenet/tracing* section:

```xml
<sensenet>
    <tracing>   
        <add key="TraceCategories" value="Query;Index;Security;System" />
    </tracing>
</sensenet>
```

The result will be a folder called *DetailedLog* in the local App_Data folder containing trace lines.

### Security activities
The *sensenet/security* section may contain the same configuration values as on the web server: *SecuritActivityTimeoutInSeconds*, *SecuritActivityLifetimeInMinutes*, *SecurityDatabaseCommandTimeoutInSeconds*, *SecurityMonitorPeriodInSeconds* are all configurable - although the default values should be fine.
