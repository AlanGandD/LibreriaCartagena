FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Cartagena.Api.csproj ./
RUN dotnet restore Cartagena.Api.csproj
COPY . ./
RUN dotnet publish Cartagena.Api.csproj -c Release -o /app/out --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Cartagena.Api.dll"]
