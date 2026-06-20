# --- Fase di build ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia il file di progetto e ripristina le dipendenze
COPY ServeurActivation.csproj ./
RUN dotnet restore

# Copia il resto del codice sorgente
COPY . ./

# Pubblica l'app in modalità Release
RUN dotnet publish -c Release -o out

# --- Fase finale (runtime) ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /src/out ./

# Esponi la porta usata dal tuo servizio (modifica se necessario)
EXPOSE 8080

# Comando di avvio (modifica "ServeurActivation.dll" se il nome differisce)
ENTRYPOINT ["dotnet", "ServeurActivation.dll"]
