# syntax=docker/dockerfile:1
ARG DOTNET_SDK=mcr.microsoft.com/dotnet/sdk:10.0-noble
ARG DOTNET_RUNTIME=mcr.microsoft.com/dotnet/aspnet:10.0-noble
ARG NODE=node:24-trixie-slim

FROM ${NODE} AS spa
WORKDIR /spa/src/DDT.Web
COPY src/DDT.Web/package.json src/DDT.Web/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci
COPY src/DDT.Web/ ./
# tsc -b checks the web's tests as well, and the tests of the theme import the design tokens' generator.
COPY src/DDT.Design/ ../DDT.Design/
# The checked in vite config writes into ../DDT.Host/wwwroot, which does not exist here.
RUN npm run build -- --outDir /spa-dist --emptyOutDir

FROM --platform=$BUILDPLATFORM ${DOTNET_SDK} AS build
ARG TARGETARCH
# The release's version, as 26.1.412. Nothing in here can count the commits, so without it Build is 0.
ARG DDT_VERSION
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Build.targets Directory.Packages.props .editorconfig ./
COPY src/DDT.Core/DDT.Core.csproj src/DDT.Core/
COPY src/DDT.Contracts/DDT.Contracts.csproj src/DDT.Contracts/
COPY src/DDT.Protocols/DDT.Protocols.csproj src/DDT.Protocols/
COPY src/DDT.Pxe/DDT.Pxe.csproj src/DDT.Pxe/
COPY src/DDT.Server/DDT.Server.csproj src/DDT.Server/
COPY src/DDT.ServiceDefaults/DDT.ServiceDefaults.csproj src/DDT.ServiceDefaults/
COPY src/DDT.Host/DDT.Host.csproj src/DDT.Host/
RUN dotnet restore src/DDT.Host/DDT.Host.csproj -a $TARGETARCH
COPY src/ src/
# DDT.Host publishes these into /app/legal.
COPY LICENSE NOTICE THIRD-PARTY-NOTICES.md ./
COPY licenses/ licenses/
COPY --from=spa /spa-dist/ src/DDT.Host/wwwroot/
RUN dotnet publish src/DDT.Host/DDT.Host.csproj -a $TARGETARCH -c Release --no-restore -o /app ${DDT_VERSION:+-p:Version=$DDT_VERSION}

FROM ${DOTNET_RUNTIME} AS final
# System.DirectoryServices.Protocols probes libldap.so.2, which the runtime image does not carry.
# Without it every LDAP call throws TypeInitializationException, including constructing a connection.
# qemu-utils and xz-utils convert uploaded qcow2 and .xz disk images, as distributions publish their cloud images.
RUN apt-get update && apt-get install -y --no-install-recommends libldap2 qemu-utils xz-utils && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
# The Windows agent and console of this release, which the server offers until others are uploaded. Only Windows
# compiles them, so the release workflow puts them into artifacts/bundle. A build without them offers none.
RUN --mount=type=bind,target=/context \
    for file in ddt-agent.exe ddt-console.zip; do \
        if [ -f "/context/artifacts/bundle/$file" ]; then cp "/context/artifacts/bundle/$file" /app/; fi; \
    done
# Defaults of the image rather than of the code: a declared Kestrel endpoint makes Kestrel ignore the URLs Aspire
# and launch profiles assign, and both certificate paths make DDT write a certificate at startup.
ENV DDT__StorePath=/var/lib/ddt \
    Kestrel__Endpoints__Https__Url=https://0.0.0.0:8443 \
    Kestrel__Certificates__Default__Path=/var/lib/ddt/certs/ddt.pem \
    Kestrel__Certificates__Default__KeyPath=/var/lib/ddt/certs/ddt-key.pem
EXPOSE 8443
ENTRYPOINT ["./DDT.Host"]
