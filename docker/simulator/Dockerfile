# Stage 1: Restore
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
# FHQ-174: build/ holds GoogleDateParsingBan.props, imported by every project that handles a Google
# date payload. It must land BEFORE restore — the import declares the analyzer PackageReference, and
# a missing import fails `dotnet publish` with MSB4019.
COPY build/ build/
COPY src/FamilyHQ.Core/FamilyHQ.Core.csproj src/FamilyHQ.Core/
COPY src/FamilyHQ.Time/FamilyHQ.Time.csproj src/FamilyHQ.Time/
COPY tools/FamilyHQ.Simulator/FamilyHQ.Simulator.csproj tools/FamilyHQ.Simulator/

RUN dotnet restore tools/FamilyHQ.Simulator/FamilyHQ.Simulator.csproj

# Stage 2: Build
FROM restore AS build
ARG VERSION=
ENV MINVER_VERSION_OVERRIDE=$VERSION
# This stage copies only the projects the Simulator references, not all of src/ — so a new project
# in its reference closure needs a line here as well as a .csproj line in the restore stage above.
COPY src/FamilyHQ.Core/ src/FamilyHQ.Core/
COPY src/FamilyHQ.Time/ src/FamilyHQ.Time/
COPY tools/FamilyHQ.Simulator/ tools/FamilyHQ.Simulator/

RUN dotnet publish tools/FamilyHQ.Simulator/FamilyHQ.Simulator.csproj -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "FamilyHQ.Simulator.dll"]
