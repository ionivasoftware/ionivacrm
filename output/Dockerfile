FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
RUN dotnet restore src/IonCrm.API/IonCrm.API.csproj
RUN dotnet publish src/IonCrm.API/IonCrm.API.csproj -c Release -o publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
CMD dotnet IonCrm.API.dll
