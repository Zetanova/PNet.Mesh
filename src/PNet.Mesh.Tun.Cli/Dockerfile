FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends iproute2 iputils-ping iperf3 netcat-openbsd \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/PNet.Mesh.Tun.Cli/PNet.Mesh.Tun.Cli.csproj", "src/PNet.Mesh.Tun.Cli/"]
COPY ["src/PNet.Mesh.Tun/PNet.Mesh.Tun.csproj", "src/PNet.Mesh.Tun/"]
COPY ["src/PNet.Mesh/PNet.Mesh.csproj", "src/PNet.Mesh/"]
RUN dotnet restore "src/PNet.Mesh.Tun.Cli/PNet.Mesh.Tun.Cli.csproj"
COPY . .
WORKDIR "/src/src/PNet.Mesh.Tun.Cli"
RUN dotnet publish "PNet.Mesh.Tun.Cli.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PNet.Mesh.Tun.Cli.dll"]
