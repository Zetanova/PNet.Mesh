#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/PNet.Mesh.TestNode/PNet.Mesh.TestNode.csproj", "src/PNet.Mesh.TestNode/"]
COPY ["src/PNet.Mesh/PNet.Mesh.csproj", "src/PNet.Mesh/"]
RUN dotnet restore "src/PNet.Mesh.TestNode/PNet.Mesh.TestNode.csproj"
COPY . .
WORKDIR "/src/src/PNet.Mesh.TestNode"
RUN dotnet build "PNet.Mesh.TestNode.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PNet.Mesh.TestNode.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PNet.Mesh.TestNode.dll"]
