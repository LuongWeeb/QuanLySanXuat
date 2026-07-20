FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["WmsMes.Web.csproj", "./"]
RUN dotnet restore "WmsMes.Web.csproj"
COPY . .
RUN dotnet build "WmsMes.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WmsMes.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WmsMes.Web.dll"]
