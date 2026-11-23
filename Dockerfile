
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

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "ComicApi.dll"]
