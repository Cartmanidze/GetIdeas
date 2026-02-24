FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY GetIdeas.sln ./
COPY GetIdeas.Worker/GetIdeas.Worker.csproj GetIdeas.Worker/
RUN dotnet restore GetIdeas.Worker/GetIdeas.Worker.csproj

COPY . .
RUN dotnet publish GetIdeas.Worker/GetIdeas.Worker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "GetIdeas.Worker.dll"]
