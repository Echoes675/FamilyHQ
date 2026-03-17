# Stage 1: Restore
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/FamilyHQ.Core/FamilyHQ.Core.csproj src/FamilyHQ.Core/
COPY tools/FamilyHQ.Simulator/FamilyHQ.Simulator.csproj tools/FamilyHQ.Simulator/

RUN dotnet restore tools/FamilyHQ.Simulator/FamilyHQ.Simulator.csproj

# Stage 2: Build
FROM restore AS build
COPY src/FamilyHQ.Core/ src/FamilyHQ.Core/
COPY tools/FamilyHQ.Simulator/ tools/FamilyHQ.Simulator/

RUN dotnet publish tools/FamilyHQ.Simulator/FamilyHQ.Simulator.csproj -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "FamilyHQ.Simulator.dll"]
