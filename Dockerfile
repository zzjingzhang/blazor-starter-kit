FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

COPY ["src/Server/Server.csproj", "src/Server/"]
COPY ["src/Application/Application.csproj", "src/Application/"]
COPY ["src/Domain/Domain.csproj", "src/Domain/"]
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Infrastructure.Shared/Infrastructure.Shared.csproj", "src/Infrastructure.Shared/"]
COPY ["src/Infrastructure/Infrastructure.csproj", "src/Infrastructure/"]
COPY ["src/Client/Client.csproj", "src/Client/"]
COPY ["src/Client.Infrastructure/Client.Infrastructure.csproj", "src/Client.Infrastructure/"]
RUN dotnet restore "src/Server/Server.csproj" --disable-parallel

COPY . .
WORKDIR /src/src/Server
RUN dotnet publish "Server.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

COPY --from=build /app/publish .
RUN mkdir -p /app/Data /app/Files
VOLUME ["/app/Data", "/app/Files"]

ENTRYPOINT ["dotnet", "BlazorHero.CleanArchitecture.Server.dll"]
