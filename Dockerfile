FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS build
WORKDIR /src

COPY LeanOptionsLab.Gateway/LeanOptionsLab.Gateway.csproj LeanOptionsLab.Gateway/
RUN dotnet restore LeanOptionsLab.Gateway/LeanOptionsLab.Gateway.csproj

COPY LeanOptionsLab.Gateway/ LeanOptionsLab.Gateway/
COPY LeanOptionsLab/Domain/ LeanOptionsLab/Domain/
RUN dotnet publish LeanOptionsLab.Gateway/LeanOptionsLab.Gateway.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:a4556ed033fa96f984bb7a8d348851cb2d36b1281dd2420070045f664fbb5f94 AS final
WORKDIR /app
COPY --from=build /out .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    LEAN_OPTIONS_LAB_RESULTS_ROOT=/results

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "LeanOptionsLab.Gateway.dll"]
