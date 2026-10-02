# Bill of Lading

ASP.NET Core MVC application targeting .NET 10.

## Run with Docker

Install Docker with Compose support. On Windows, start Docker Desktop and select
Linux containers. Run these commands from the repository root:

```powershell
docker compose up --build -d
```

Open http://localhost:8080.

View logs and stop the application:

```powershell
docker compose logs -f lading-system
docker compose down
```

The image uses a multi-stage build and runs as the built-in non-root `app` user.
Only the published application and ASP.NET Core runtime are in the final image.
Local build output is excluded from the build context.

## Development with hot reload

From the repository root, run:

```powershell
docker compose -f compose.yaml -f compose.dev.yaml up --build
```

Open http://localhost:8080, then edit files in `LadingSystem`. The development
stage runs `dotnet watch` using the .NET SDK and a bind mount of your source.
Polling detects edits on Docker Desktop; separate volumes keep container `bin`
and `obj` output isolated from Windows builds. The launch profile is disabled so
the app uses container port 8080. Supported changes apply through hot reload;
changes that require a restart automatically restart the app. Refresh the browser
if it does not update automatically. Rebuild after changing the Dockerfile.

Stop the development container with:

```powershell
docker compose -f compose.yaml -f compose.dev.yaml down
```

The default Compose command still builds the production runtime stage.

## Run without Compose

```powershell
docker build -f LadingSystem/DockerFile -t lading-system:local .
docker run --rm -p 8080:8080 --name lading-system lading-system:local
```

## Configuration

The container runs in `Production` and listens on HTTP port 8080. Override settings
with environment variables in `compose.yaml` or `docker run -e`; use double
underscores for nested .NET configuration keys, such as `Logging__LogLevel__Default`.
Change the first port in `8080:8080` to use a different host port.

HTTPS certificates are not bundled. For an HTTPS deployment, configure TLS at your
hosting proxy or configure ASP.NET Core HTTPS with a mounted certificate. The
existing HTTPS redirection middleware cannot redirect until an HTTPS port is
configured; the default container setup serves HTTP.
