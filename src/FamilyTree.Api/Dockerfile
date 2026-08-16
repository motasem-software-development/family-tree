FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/ ./src/
RUN dotnet publish src/FamilyTree.Api/FamilyTree.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Runs unprivileged: a container compromise should not also be a root compromise.
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "FamilyTree.Api.dll"]
