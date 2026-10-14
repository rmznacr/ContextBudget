FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ContextBudget.slnx ./
COPY src/ContextBudget.Domain/ src/ContextBudget.Domain/
COPY src/ContextBudget.Application/ src/ContextBudget.Application/
COPY src/ContextBudget.Infrastructure/ src/ContextBudget.Infrastructure/
COPY src/ContextBudget.Api/ src/ContextBudget.Api/
RUN dotnet publish src/ContextBudget.Api/ContextBudget.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ContextBudget.Api.dll"]
