# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/TrebynGibService/TrebynGibService.csproj src/TrebynGibService/
RUN dotnet restore src/TrebynGibService/TrebynGibService.csproj
COPY src/ src/
RUN dotnet publish src/TrebynGibService/TrebynGibService.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# The aspnet image has neither curl nor wget; bash's /dev/tcp is enough for /health.
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\n\\r\\n' >&3 && grep -q OK <&3"]
USER $APP_UID
ENTRYPOINT ["dotnet", "TrebynGibService.dll"]
