# Lucene 2.9 for sensenet ECM
A sensenet ECM *Search engine* implementation built on *Lucene.Net 2.9*

[![NuGet](https://img.shields.io/nuget/v/SenseNet.Search.Lucene29.svg)](https://www.nuget.org/packages/SenseNet.Search.Lucene29)
[![NuGet](https://img.shields.io/nuget/v/SenseNet.Search.Lucene29.Common.svg)](https://www.nuget.org/packages/SenseNet.Search.Lucene29.Common)
[![NuGet](https://img.shields.io/nuget/v/SenseNet.Search.Lucene29.Local.svg)](https://www.nuget.org/packages/SenseNet.Search.Lucene29.Local)

### SenseNet.Search.Lucene29 library
This library contains all the Lucene-related code that works directly with the index stored in the file system. It does not have a dependency on the _Content Repository_, only the general _Search_ library for sensenet ECM.

### Common library
This is the server-side component of the Lucene search engine implementation: contains classes that are needed on the web server.

### Local library
A query and indexing engine that is able to work *locally, on the web server*. This is the original implementation for sensenet ECM that requires the index folder to be present on *all web servers* in an NLB environment.

## Running tests
The test environment currently needs the base content type definitions to build an _in-memory database_. To achieve this, you will need to copy the contents of the following folder from the Services repository to the **same directory** in this repository in your local machine.

`\src\nuget\snadmin\install-services\import\System\Schema\ContentTypes` 

After this you should be able to run all the tests in this repository. Please **do not commit this folder** to this repo so that we avoid data duplication.
