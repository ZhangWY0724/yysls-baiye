FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY apps/api/Yanyun.Api.csproj apps/api/packages.lock.json apps/api/
RUN dotnet restore apps/api/Yanyun.Api.csproj --locked-mode
COPY apps/api/ apps/api/
RUN dotnet publish apps/api/Yanyun.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
RUN mkdir -p /app/keys && chown app:app /app/keys
USER $APP_UID
ENTRYPOINT ["dotnet", "Yanyun.Api.dll"]
