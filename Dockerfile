
# Stage 1: Build ASP.NET Core application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ComicApi/ComicApi.csproj ComicApi/
RUN dotnet restore ComicApi/ComicApi.csproj

COPY . .
ARG SixLaborsLicenseKey
RUN printf '%s' "$SixLaborsLicenseKey" > /tmp/sixlabors.lic \
    && unset SixLaborsLicenseKey \
    && dotnet publish ComicApi/ComicApi.csproj -c Release -o /app/publish --no-restore -p:SixLaborsLicenseFile=/tmp/sixlabors.lic \
    && rm /tmp/sixlabors.lic

# Stage 2: Run application
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app/publish .
COPY docker-entrypoint.sh /app/docker-entrypoint.sh
RUN mkdir -p /app/media-seed \
    && mv /app/wwwroot/uploads /app/media-seed/uploads \
    && mv /app/wwwroot/img/genres /app/media-seed/genres \
    && ln -s /data/uploads /app/wwwroot/uploads \
    && ln -s /data/genres /app/wwwroot/img/genres \
    && sed -i 's/\r$//' /app/docker-entrypoint.sh

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["sh", "/app/docker-entrypoint.sh"]
