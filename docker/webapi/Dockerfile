# Stage 1: Restore
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/FamilyHQ.Core/FamilyHQ.Core.csproj src/FamilyHQ.Core/
COPY src/FamilyHQ.Data/FamilyHQ.Data.csproj src/FamilyHQ.Data/
COPY src/FamilyHQ.Data.PostgreSQL/FamilyHQ.Data.PostgreSQL.csproj src/FamilyHQ.Data.PostgreSQL/
COPY src/FamilyHQ.Services/FamilyHQ.Services.csproj src/FamilyHQ.Services/
COPY src/FamilyHQ.WebApi/FamilyHQ.WebApi.csproj src/FamilyHQ.WebApi/

RUN dotnet restore src/FamilyHQ.WebApi/FamilyHQ.WebApi.csproj

# Stage 2: Build
FROM restore AS build
ARG VERSION=
ENV MINVER_VERSION_OVERRIDE=$VERSION
COPY src/ src/

RUN dotnet publish src/FamilyHQ.WebApi/FamilyHQ.WebApi.csproj -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "FamilyHQ.WebApi.dll"]
