# Construído a partir da RAIZ do repositório, não desta pasta:
#   docker build -f src/Fiscal.Api/Dockerfile .
# O compose já faz isso (context: . e dockerfile apontando para cá).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Os csproj vêm antes do resto do código de propósito: enquanto as dependências não
# mudarem, o restore fica em cache e uma alteração de código não obriga a baixar
# todos os pacotes de novo.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Fiscal.Domain/Fiscal.Domain.csproj src/Fiscal.Domain/
COPY src/Fiscal.Application/Fiscal.Application.csproj src/Fiscal.Application/
COPY src/Fiscal.Infrastructure/Fiscal.Infrastructure.csproj src/Fiscal.Infrastructure/
COPY src/Fiscal.Api/Fiscal.Api.csproj src/Fiscal.Api/

RUN dotnet restore src/Fiscal.Api/Fiscal.Api.csproj

COPY src/ src/

RUN dotnet publish src/Fiscal.Api/Fiscal.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# Imagem de runtime: só o ASP.NET, sem SDK nem código-fonte.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app .

# Usuário sem privilégio, já presente na imagem base. Container de aplicação não tem
# motivo para rodar como root.
USER $APP_UID

EXPOSE 8080

ENTRYPOINT ["dotnet", "Fiscal.Api.dll"]
