FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy Directory.Build.props so project-level defaults are available during restore
COPY Directory.Build.props .

# Copy .csproj files first to take advantage of Docker layer caching for restore
COPY Tubester.Api/Tubester.Api.csproj Tubester.Api/
COPY Tubester.Abstractions/Tubester.Abstractions.csproj Tubester.Abstractions/
COPY Tubester.Application/Tubester.Application.csproj Tubester.Application/
COPY Tubester.Domain/Tubester.Domain.csproj Tubester.Domain/
COPY Tubester.Integration/Tubester.Integration.csproj Tubester.Integration/
COPY Tubester.Persistence/Tubester.Persistence.csproj Tubester.Persistence/

RUN dotnet restore Tubester.Persistence/Tubester.Persistence.csproj
RUN dotnet restore Tubester.Api/Tubester.Api.csproj

# Copy source for all projects in the dependency graph
COPY Tubester.Api/ Tubester.Api/
COPY Tubester.Abstractions/ Tubester.Abstractions/
COPY Tubester.Application/ Tubester.Application/
COPY Tubester.Domain/ Tubester.Domain/
COPY Tubester.Integration/ Tubester.Integration/
COPY Tubester.Persistence/ Tubester.Persistence/

RUN dotnet publish Tubester.Api/Tubester.Api.csproj \
    -c Release \
    -o /app \
    --no-restore

# Build EF Core migrations bundle
RUN dotnet tool install --global dotnet-ef
ENV PATH="$PATH:/root/.dotnet/tools"

RUN dotnet ef migrations bundle \
    --project Tubester.Persistence/Tubester.Persistence.csproj \
    --startup-project Tubester.Api/Tubester.Api.csproj \
    --output /app/efbundle \
    --force

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://0.0.0.0:5000
EXPOSE 5000

USER $APP_UID

ENTRYPOINT ["dotnet", "Tubester.Api.dll"]