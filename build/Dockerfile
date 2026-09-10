# syntax=docker/dockerfile:1
ARG DOTNET_SDK=mcr.microsoft.com/dotnet/sdk:10.0-noble
ARG DOTNET_RUNTIME=mcr.microsoft.com/dotnet/aspnet:10.0-noble
ARG NODE=node:24-trixie-slim

FROM ${NODE} AS spa
WORKDIR /spa
COPY src/DDT.Web/package.json src/DDT.Web/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci
COPY src/DDT.Web/ ./
# The checked in vite config writes into ../DDT.Host/wwwroot, which does not exist here.
RUN npm run build -- --outDir /spa-dist --emptyOutDir

FROM --platform=$BUILDPLATFORM ${DOTNET_SDK} AS build
ARG TARGETARCH
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/DDT.Core/DDT.Core.csproj src/DDT.Core/
COPY src/DDT.Contracts/DDT.Contracts.csproj src/DDT.Contracts/
COPY src/DDT.Protocols/DDT.Protocols.csproj src/DDT.Protocols/
COPY src/DDT.Pxe/DDT.Pxe.csproj src/DDT.Pxe/
COPY src/DDT.Server/DDT.Server.csproj src/DDT.Server/
COPY src/DDT.ServiceDefaults/DDT.ServiceDefaults.csproj src/DDT.ServiceDefaults/
COPY src/DDT.Host/DDT.Host.csproj src/DDT.Host/
RUN dotnet restore src/DDT.Host/DDT.Host.csproj -a $TARGETARCH
COPY src/ src/
COPY --from=spa /spa-dist/ src/DDT.Host/wwwroot/
RUN dotnet publish src/DDT.Host/DDT.Host.csproj -a $TARGETARCH -c Release --no-restore -o /app

FROM ${DOTNET_RUNTIME} AS final
# System.DirectoryServices.Protocols probes libldap.so.2, which the runtime image does not carry.
# Without it every LDAP call throws TypeInitializationException, including constructing a connection.
RUN apt-get update && apt-get install -y --no-install-recommends libldap2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8443
ENTRYPOINT ["./DDT.Host"]
