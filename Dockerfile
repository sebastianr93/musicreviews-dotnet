# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Etapa de compilacion
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Los archivos de proyecto van primero y solos. Docker cachea cada capa por el
# contenido de lo que se copio: si solo cambio codigo, esta capa se reusa y el
# restore —que es lo que baja paquetes de la red— no se vuelve a ejecutar.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/MusicReviews.Domain/MusicReviews.Domain.csproj                 src/MusicReviews.Domain/
COPY src/MusicReviews.Application/MusicReviews.Application.csproj       src/MusicReviews.Application/
COPY src/MusicReviews.Infrastructure/MusicReviews.Infrastructure.csproj src/MusicReviews.Infrastructure/
COPY src/MusicReviews.Api/MusicReviews.Api.csproj                       src/MusicReviews.Api/

RUN dotnet restore src/MusicReviews.Api/MusicReviews.Api.csproj

# Recien ahora el codigo. Los tests no se copian: la imagen no los necesita y
# traerlos invalidaria esta capa cada vez que se toca un test.
COPY src/ src/

RUN dotnet publish src/MusicReviews.Api/MusicReviews.Api.csproj \
    -c Release \
    -o /app \
    --no-restore \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Etapa de ejecucion
# ---------------------------------------------------------------------------
# Imagen sin SDK ni compilador: ~110 MB contra ~900 MB. Menos superficie que
# atacar y despliegues mas rapidos.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# InvariantGlobalization=true esta activo en Directory.Build.props, asi que la
# imagen no necesita icu. Se declara igual para que quede explicito: si algun dia
# se desactiva esa propiedad, la app falla al arrancar aca y no en produccion.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0

# El contenedor no escribe nada en disco. Los avatares subidos —lo unico que carga
# un usuario— van a la base, justamente porque el filesystem de Cloud Run es
# efimero: se recrea en cada version publicada y en cada arranque en frio.
COPY --from=build /app ./

# Usuario sin privilegios. La imagen de Microsoft ya trae "app" (uid 64198).
USER app

# El puerto real lo decide la plataforma por la variable PORT; esto es solo el
# valor por defecto para correr la imagen a mano.
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MusicReviews.Api.dll"]
