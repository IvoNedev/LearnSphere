FROM node:24-alpine AS web
WORKDIR /web
COPY src/LearnSphere.Web/package*.json ./
RUN npm ci
COPY src/LearnSphere.Web/ ./
RUN npx ng build --configuration production --output-path /web/out

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/LearnSphere.Api/LearnSphere.Api.csproj src/LearnSphere.Api/
RUN dotnet restore src/LearnSphere.Api/LearnSphere.Api.csproj
COPY src/LearnSphere.Api/ src/LearnSphere.Api/
RUN dotnet publish src/LearnSphere.Api -c Release -o /app --no-restore
COPY --from=web /web/out/browser/ /app/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
RUN mkdir -p /app/App_Data && chown -R $APP_UID /app/App_Data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s CMD ["dotnet", "LearnSphere.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "LearnSphere.Api.dll"]
