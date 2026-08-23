# Stage 1: Restore
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
# FHQ-174: build/ holds GoogleDateParsingBan.props, imported by every project that handles a Google
# date payload. It must land BEFORE restore — the import declares the analyzer PackageReference, and
# a missing import fails `dotnet publish` with MSB4019.
COPY build/ build/
COPY src/FamilyHQ.Core/FamilyHQ.Core.csproj src/FamilyHQ.Core/
COPY src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj src/FamilyHQ.WebUi/

RUN dotnet restore src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj

# Stage 2: Build
FROM restore AS build
ARG VERSION=
ENV MINVER_VERSION_OVERRIDE=$VERSION
COPY src/FamilyHQ.Core/ src/FamilyHQ.Core/
COPY src/FamilyHQ.WebUi/ src/FamilyHQ.WebUi/

RUN dotnet publish src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj -c Release -o /app/publish

# Stage 3: Runtime
FROM nginx:alpine AS runtime

COPY docker/webui/nginx.conf /etc/nginx/conf.d/default.conf
COPY docker/webui/docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
RUN chmod -R o+r /usr/share/nginx/html && find /usr/share/nginx/html -type d -exec chmod o+x {} +

EXPOSE 80
ENTRYPOINT ["/docker-entrypoint.sh"]
