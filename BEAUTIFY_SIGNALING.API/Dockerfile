﻿FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BEAUTIFY_SIGNALING.API/BEAUTIFY_SIGNALING.API.csproj", "BEAUTIFY_SIGNALING.API/"]
COPY ["BEAUTIFY_SIGNALING.SERVICES/BEAUTIFY_SIGNALING.SERVICES.csproj", "BEAUTIFY_SIGNALING.SERVICES/"]
COPY ["BEAUTIFY_SIGNALING.REPOSITORY/BEAUTIFY_SIGNALING.REPOSITORY.csproj", "BEAUTIFY_SIGNALING.REPOSITORY/"]
RUN dotnet restore "BEAUTIFY_SIGNALING.API/BEAUTIFY_SIGNALING.API.csproj"
COPY . .
WORKDIR "/src/BEAUTIFY_SIGNALING.API"
RUN dotnet build "BEAUTIFY_SIGNALING.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BEAUTIFY_SIGNALING.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BEAUTIFY_SIGNALING.API.dll"]
