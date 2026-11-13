FROM node:24-alpine AS styles
WORKDIR /src
RUN npm install --global pnpm@10.20.0
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY src/TempStore/Styles/ src/TempStore/Styles/
COPY src/TempStore/Components/ src/TempStore/Components/
RUN mkdir -p src/TempStore/wwwroot && pnpm run build:css

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# .NET 10 needs Razor files present during restore to include Blazor assets.
COPY src/TempStore/ src/TempStore/
RUN dotnet restore src/TempStore/TempStore.csproj
COPY --from=styles /src/src/TempStore/wwwroot/app.css src/TempStore/wwwroot/app.css
RUN dotnet publish src/TempStore/TempStore.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000
COPY --from=build /app/publish/ ./
ENTRYPOINT ["dotnet", "TempStore.dll"]
