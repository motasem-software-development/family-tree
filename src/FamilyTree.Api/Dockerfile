FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/ ./src/
RUN dotnet publish src/FamilyTree.Api/FamilyTree.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# SkiaSharp draws the PDF export through a native library the aspnet image does not ship.
# libfontconfig1 and libfreetype6 are what libSkiaSharp links against for text; without them
# the export endpoint throws DllNotFoundException at first use, long after startup succeeded.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 libfreetype6 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Runs unprivileged: a container compromise should not also be a root compromise.
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "FamilyTree.Api.dll"]
