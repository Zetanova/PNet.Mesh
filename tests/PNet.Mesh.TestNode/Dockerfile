#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:5.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["tests/PNet.Mesh.TestNode/PNet.Mesh.TestNode.csproj", "tests/PNet.Mesh.TestNode/"]
RUN dotnet restore "tests/PNet.Mesh.TestNode/PNet.Mesh.TestNode.csproj"
COPY . .
WORKDIR "/src/tests/PNet.Mesh.TestNode"
RUN dotnet build "PNet.Mesh.TestNode.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PNet.Mesh.TestNode.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PNet.Mesh.TestNode.dll"]