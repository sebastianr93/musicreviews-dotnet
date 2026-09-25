# Despliegue en Google Cloud Run + Neon

Guia completa del despliegue de la instancia publica. El objetivo es que la aplicacion
quede en linea, con catalogo cargado, sin costo y sin sorpresas en la factura.

**Resumen de la arquitectura desplegada:**

```
Navegador → Cloud Run (contenedor, escala a cero) → Neon (PostgreSQL administrado)
                     ↓
              MusicBrainz / Cover Art Archive / feeds RSS
```

---

## Por que esta combinacion

**Cloud Run** ejecuta el contenedor tal cual y escala a cero cuando nadie lo usa. El
arranque en frio es de uno a dos segundos, contra los cuarenta y pico de los planes
gratuitos que suspenden el proceso. Para un enlace que alguien abre una vez, esa
diferencia es la que separa "cargo" de "esta roto".

**Neon** da PostgreSQL administrado sin vencimiento en el plan gratuito. Importa la
aclaracion: varias plataformas ofrecen base gratis que **caduca a los treinta dias** y
despues borra los datos.

**Region.** Conviene poner las dos en la costa este de Estados Unidos. La latencia que
importa no es la del visitante hacia la aplicacion —se paga una vez por peticion— sino la
de la aplicacion hacia la base, que se paga en cada consulta. Ponerlas juntas vale mucho
mas que acercar el servidor al usuario.

---

## 1. Base de datos en Neon

1. Crear una cuenta en [neon.com](https://neon.com) (sin tarjeta) y un proyecto nuevo.
2. Elegir region en la costa este de Estados Unidos.
3. Copiar la cadena de conexion. Tiene esta forma:

```
postgresql://usuario:clave@ep-algo-123456.us-east-2.aws.neon.tech/neondb?sslmode=require
```

Esa cadena va tal cual en `DATABASE_URL`. La aplicacion la traduce al formato que espera
Npgsql (`DatabaseUrl`), asi que no hay que desarmarla a mano.

> **Neon suspende el computo tras unos minutos sin uso** y lo despierta con la primera
> consulta. Suma unos cientos de milisegundos a la primera peticion despues de un rato de
> inactividad. Es parte del plan gratuito y no hay nada que configurar.

---

## 2. Preparar la base desde la maquina local

Este paso se hace **una sola vez** y evita dos problemas del entorno de produccion.

### 2.1 Generar la migracion de los avatares

```bash
dotnet ef migrations add AddUserAvatars --project src/MusicReviews.Infrastructure --startup-project src/MusicReviews.Api --output-dir Persistence/Migrations
```

### 2.2 Aplicar el esquema y sembrar el contenido

Con la cadena de Neon en el entorno, se levanta la aplicacion local apuntando a la base
remota. Al arrancar aplica las migraciones, crea los roles, crea el administrador y
siembra el contenido de demostracion.

En PowerShell:

```powershell
$env:DATABASE_URL = "postgresql://usuario:clave@ep-algo.us-east-2.aws.neon.tech/neondb?sslmode=require"
$env:Jwt__SigningKey = "una-clave-larga-y-aleatoria-de-al-menos-32-caracteres"
$env:SeedAdmin__UserName = "admin"
$env:SeedAdmin__Email = "admin@tudominio.com"
$env:SeedAdmin__Password = "una-clave-fuerte"
$env:Demo__Enabled = "true"
$env:Demo__Password = "Demo1234!"
$env:ASPNETCORE_ENVIRONMENT = "Production"

dotnet run --project src/MusicReviews.Api
```

Dejarlo corriendo unos dos minutos. En el log se ve el avance de la siembra; termina con
`Contenido de demostracion listo.` Ahi se corta con `Ctrl` + `C`.

**Por que aca y no en produccion.** Cloud Run **estrangula la CPU del contenedor cuando no
esta atendiendo una peticion**. `DemoSeeder` tarda cerca de un minuto —son unas cuarenta
consultas a MusicBrainz y el limite es de una por segundo— y en Cloud Run nunca
terminaria. Sembrar desde la maquina local resuelve eso sin agregar configuracion, y de
paso deja la base lista antes del primer despliegue.

---

## 3. Preparar Google Cloud

1. Crear una cuenta en [console.cloud.google.com](https://console.cloud.google.com). Pide
   tarjeta para verificar identidad; hace una retencion de aproximadamente un dolar que se
   libera sola.
2. Crear un proyecto nuevo.
3. Instalar [gcloud CLI](https://cloud.google.com/sdk/docs/install) y autenticarse:

```bash
gcloud auth login
gcloud config set project EL-ID-DE-TU-PROYECTO
gcloud services enable run.googleapis.com cloudbuild.googleapis.com artifactregistry.googleapis.com
```

### 3.1 Poner los frenos ANTES de desplegar

Estos dos pasos no son opcionales. El free tier de Cloud Run no vence, pero **si se
excede, se cobra**, y la forma realista de excederlo no es el uso normal: es que alguien
raspe la aplicacion o le tire volumen y Cloud Run escale solo para atender.

**Limite de instancias** (se aplica en el despliegue del paso siguiente con
`--max-instances=1`): el peor caso pasa a ser una aplicacion lenta, no cien contenedores
facturando.

**Alerta de presupuesto:**

```bash
gcloud billing budgets create \
  --billing-account=$(gcloud billing accounts list --format="value(name)" --limit=1) \
  --display-name="MusicReviews" \
  --budget-amount=1USD \
  --threshold-rule=percent=0.5 \
  --threshold-rule=percent=1.0
```

Tambien se puede hacer desde la consola, en Facturacion → Presupuestos y alertas. Llega un
correo antes de que exista un problema.

---

## 4. Desplegar

Desde la raiz del repositorio:

```bash
gcloud run deploy musicreviews \
  --source . \
  --region us-east1 \
  --allow-unauthenticated \
  --max-instances=1 \
  --memory=512Mi \
  --cpu-boost \
  --set-env-vars="ASPNETCORE_ENVIRONMENT=Production" \
  --set-env-vars="DATABASE_URL=postgresql://usuario:clave@ep-algo.us-east-2.aws.neon.tech/neondb?sslmode=require" \
  --set-env-vars="Jwt__SigningKey=la-misma-clave-del-paso-2" \
  --set-env-vars="Database__MigrateOnStartup=false" \
  --set-env-vars="MusicBrainz__UserAgent=MusicReviews/1.0.0 ( https://github.com/sebastianr93/musicreviews-dotnet )" \
  --set-env-vars="News__UserAgent=MusicReviews/1.0.0 ( https://github.com/sebastianr93/musicreviews-dotnet )"
```

`--source .` hace que Cloud Build compile el `Dockerfile` del repositorio y suba la imagen;
no hace falta tener Docker instalado.

Al terminar imprime la URL del servicio. Esa es la aplicacion.

### Que hace cada opcion

| Opcion | Por que |
|--------|---------|
| `--max-instances=1` | El freno de gasto. Sin esto, un pico de trafico escala y factura. |
| `--memory=512Mi` | Alcanza de sobra: el `Dockerfile` usa GC de workstation (`DOTNET_gcServer=0`), que es el modo de bajo consumo. |
| `--cpu-boost` | CPU extra durante el arranque. Recorta el arranque en frio a la mitad. |
| `--allow-unauthenticated` | Es un sitio publico. Sin esto, Cloud Run exige credenciales de Google para entrar. |
| `Database__MigrateOnStartup=false` | Las migraciones ya se aplicaron en el paso 2. Evita una consulta a la base en cada arranque en frio. |
| `Jwt__SigningKey` | **La misma del paso 2.** Si cambia, todas las sesiones existentes dejan de validar. |
| Sin `Demo__*` | La siembra ya se hizo. En Cloud Run no terminaria. |

`PORT` no se declara: lo inyecta Cloud Run y la aplicacion lo lee.

---

## 5. Verificar

```bash
curl https://TU-URL.run.app/api/health
```

Tiene que devolver `database: "up"`. Despues, en el navegador:

- `/` — el home tiene que mostrar tapas, no estar vacio.
- `/scalar/v1` — la documentacion de la Api.
- Entrar con una cuenta de demostracion (`celeste`, `bruno`, `lucia`, `tomas`, `mora`,
  `ivan`) y la contraseña de `Demo__Password`.
- Subir un avatar, recargar, y volver a entrar despues de un rato: la foto tiene que
  seguir ahi. Es lo que confirma que el almacenamiento en base funciona y que el
  filesystem efimero dejo de importar.

---

## 6. Actualizar

Cada version nueva es el mismo comando del paso 4. Las variables de entorno ya
configuradas se conservan, asi que alcanza con:

```bash
gcloud run deploy musicreviews --source . --region us-east1
```

Si una version trae migraciones, se aplican antes desde la maquina local con la
`DATABASE_URL` de Neon:

```bash
dotnet ef database update --project src/MusicReviews.Infrastructure --startup-project src/MusicReviews.Api
```

El orden importa: primero la migracion, despues el despliegue. Al reves, la version nueva
del codigo arranca contra el esquema viejo.

---

## Costos reales

| Recurso | Free tier | Consumo esperado |
|---------|-----------|------------------|
| Cloud Run | 2 M peticiones y 180.000 vCPU-segundos por mes, sin vencimiento | Muy por debajo |
| Cloud Build | 120 minutos de compilacion por dia | Unos 3 minutos por despliegue |
| Artifact Registry | 0,5 GB | La imagen pesa unos 120 MB; conviene borrar versiones viejas de vez en cuando |
| Neon | 3 GiB de datos, sin vencimiento | Los avatares son lo unico que crece, con tope de 2 MB cada uno |

Con el limite de instancias y la alerta de presupuesto configurados, el costo esperado es
cero.

---

## Cuando esto deje de alcanzar

Las tres cosas que habria que cambiar para subir de escala, en orden:

1. **La cola de MusicBrainz es global al proceso.** Con mas de una instancia, cada una
   tendria su propia cola y entre todas superarian la peticion por segundo. Hay que mover
   el control a un recurso compartido.
2. **Las migraciones se aplican a mano.** Con despliegues frecuentes conviene un job
   separado que corra antes del despliegue, no un paso manual.
3. **Los avatares estan en la base.** Funciona con este volumen; a partir de cierto
   tamanio corresponde un bucket de objetos. `IAvatarStorage` es la unica pieza que
   cambia.
