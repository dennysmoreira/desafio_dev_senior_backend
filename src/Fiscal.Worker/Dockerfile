# Construído a partir da RAIZ do repositório:
#   docker build -f src/Fiscal.Worker/Dockerfile .
# Mesma estrutura do Dockerfile da API; muda só o projeto publicado.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Fiscal.Domain/Fiscal.Domain.csproj src/Fiscal.Domain/
COPY src/Fiscal.Application/Fiscal.Application.csproj src/Fiscal.Application/
COPY src/Fiscal.Infrastructure/Fiscal.Infrastructure.csproj src/Fiscal.Infrastructure/
COPY src/Fiscal.Worker/Fiscal.Worker.csproj src/Fiscal.Worker/

RUN dotnet restore src/Fiscal.Worker/Fiscal.Worker.csproj

COPY src/ src/

RUN dotnet publish src/Fiscal.Worker/Fiscal.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# Sem ASP.NET: o worker não serve HTTP, então a imagem base é a de runtime puro.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

COPY --from=build /app .

USER $APP_UID

ENTRYPOINT ["dotnet", "Fiscal.Worker.dll"]
