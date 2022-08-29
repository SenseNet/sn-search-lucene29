#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:3.1 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:3.1 AS build
WORKDIR /src
COPY ["SenseNet.Search.Lucene29.Centralized.GrpcService/SenseNet.Search.Lucene29.Centralized.GrpcService.csproj", "SenseNet.Search.Lucene29.Centralized.GrpcService/"]
COPY ["SenseNet.Search.Lucene29.Centralized.Index/SenseNet.Search.Lucene29.Centralized.Index.csproj", "SenseNet.Search.Lucene29.Centralized.Index/"]
COPY ["SenseNet.Search.Lucene29.Centralized.Common/SenseNet.Search.Lucene29.Centralized.Common.csproj", "SenseNet.Search.Lucene29.Centralized.Common/"]
COPY ["SenseNet.Search.Lucene29/SenseNet.Search.Lucene29.csproj", "SenseNet.Search.Lucene29/"]
RUN dotnet restore "SenseNet.Search.Lucene29.Centralized.GrpcService/SenseNet.Search.Lucene29.Centralized.GrpcService.csproj"
COPY . .
WORKDIR "/src/SenseNet.Search.Lucene29.Centralized.GrpcService"
RUN dotnet build "SenseNet.Search.Lucene29.Centralized.GrpcService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SenseNet.Search.Lucene29.Centralized.GrpcService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SenseNet.Search.Lucene29.Centralized.GrpcService.dll"]