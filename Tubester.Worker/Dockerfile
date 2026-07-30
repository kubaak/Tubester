FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy repo-level build files first
COPY Directory.Build.props .

# If you have these files, uncomment/copy them too:
# COPY Directory.Packages.props .
# COPY NuGet.config .
# COPY global.json .

# Copy project files first to improve Docker layer caching
COPY Tubester.Worker/Tubester.Worker.csproj Tubester.Worker/
COPY Tubester.Abstractions/Tubester.Abstractions.csproj Tubester.Abstractions/
COPY Tubester.Application/Tubester.Application.csproj Tubester.Application/
COPY Tubester.Domain/Tubester.Domain.csproj Tubester.Domain/
COPY Tubester.Integration/Tubester.Integration.csproj Tubester.Integration/
COPY Tubester.Persistence/Tubester.Persistence.csproj Tubester.Persistence/
COPY Tubester.Observability/Tubester.Observability.csproj Tubester.Observability/

# Restore only the Worker project; it restores referenced projects too
RUN dotnet restore Tubester.Worker/Tubester.Worker.csproj

# Copy source after restore
COPY Tubester.Worker/ Tubester.Worker/
COPY Tubester.Abstractions/ Tubester.Abstractions/
COPY Tubester.Application/ Tubester.Application/
COPY Tubester.Domain/ Tubester.Domain/
COPY Tubester.Integration/ Tubester.Integration/
COPY Tubester.Persistence/ Tubester.Persistence/
COPY Tubester.Observability/ Tubester.Observability/

RUN dotnet publish Tubester.Worker/Tubester.Worker.csproj \
    -c Release \
    -o /app \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "Tubester.Worker.dll"]