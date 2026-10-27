FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8008
ENV ASPNETCORE_URLS=http://+:8008

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Wallet.Api/Wallet.Api.csproj", "Wallet.Api/"]
COPY ["Wallet.Application/Wallet.Application.csproj", "Wallet.Application/"]
COPY ["Wallet.Domain/Wallet.Domain.csproj", "Wallet.Domain/"]
COPY ["Wallet.Infrastructure/Wallet.Infrastructure.csproj", "Wallet.Infrastructure/"]
RUN dotnet restore "Wallet.Api/Wallet.Api.csproj"
COPY . .
RUN dotnet build "Wallet.Api/Wallet.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Wallet.Api/Wallet.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Wallet.Api.dll"]
