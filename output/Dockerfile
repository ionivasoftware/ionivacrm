FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
RUN dotnet restore src/IonCrm.API/IonCrm.API.csproj
RUN dotnet publish src/IonCrm.API/IonCrm.API.csproj -c Release -o publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "IonCrm.API.dll"]
