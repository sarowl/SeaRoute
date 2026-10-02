# Bill of Lading

ASP.NET Core MVC application targeting .NET 10.

## Firebase authentication

Firebase Authentication in `searoute-d3c1b` handles email/password signup, login,
Google sign-in, and password credentials. Supabase PostgreSQL stores user
profiles and all application data through the existing EF Core models and
migrations. Firestore is not used. Existing Firestore data is not deleted.

After Firebase sign-in, the browser sends an ID token in the Authorization
header. The .NET backend verifies it with Firebase Admin SDK, including revoked
tokens and disabled accounts. It derives UID, email, and initial display name
from that verified token and creates or retrieves `Users` by the unique
`FirebaseUid`. Browser-supplied UID or profile fields never determine identity.
An email collision with another UID is rejected rather than linking accounts.
The session cookie is issued only after PostgreSQL provisioning succeeds.
The dashboard renders the PostgreSQL profile, and application code can use
the local integer user ID from the cookie's NameIdentifier claim.

The token exchange also requires an antiforgery token. The cookie is HTTP-only.
The cookie expires with the ID token (about one hour); Remember me persists it
across browser restarts within that period. Sign out clears both sessions.
If database provisioning fails after Firebase signup, sign in again to retry;
the Firebase account does not need to be recreated. Password reset/change
operations belong to Firebase Authentication, not the PostgreSQL database.

### Local setup

1. Keep the public web SDK settings in the `Firebase` section of appsettings.
2. Configure Firebase Admin [Application Default Credentials](https://firebase.google.com/docs/admin/setup#initialize_the_sdk_in_non-google_environments).
   For a service-account JSON file from the existing Firebase project's
   Settings > Service accounts, store it outside the repository and set
   `GOOGLE_APPLICATION_CREDENTIALS` to its absolute path. The service account
   needs permission to read Firebase Authentication users for revocation checks.
   Firebase CLI login alone does not configure Admin SDK credentials.
3. Supply the Supabase PostgreSQL connection string as
   `ConnectionStrings__DefaultConnection` or a .NET user secret. In Development,
   the existing ignored `.env` file is also supported with `host`, `port`,
   `database`, `user`, `password`, and `sslmode` fields. An explicit connection
   string takes precedence. Production requires an explicit connection string.
4. Apply the existing migrations with `dotnet ef database update`, then restart
   the app. The `Users` table needs `FullName` and the unique `FirebaseUid` index.

PowerShell example (use your actual file path and secret connection string):

```powershell
$env:GOOGLE_APPLICATION_CREDENTIALS = 'C:\secure\searoute-admin.json'
$env:ConnectionStrings__DefaultConnection = '<Supabase PostgreSQL connection string>'
dotnet run
```

Never commit passwords, tokens, or service-account keys. Rotate any password
previously stored in source-controlled configuration. Admin credentials and
database credentials stay on the backend and are never rendered into pages.

### Sign-in troubleshooting

If Google sign-in completes but `/Auth/Csrf` returns **503**, read the .NET server
log. Missing or unreadable Admin credentials now produce explicit setup guidance.
Open the existing project's [service accounts settings](https://console.firebase.google.com/project/searoute-d3c1b/settings/serviceaccounts/adminsdk),
generate an Admin SDK private key if you do not already have one, and store the
JSON outside the repository. Set `GOOGLE_APPLICATION_CREDENTIALS` in the process
that starts .NET, then restart it. A variable set in a terminal does not affect
an already-running IDE or app; run `dotnet run` from that terminal or restart
the IDE with the configured environment. The public browser API key and a
Firebase CLI login do not replace Admin credentials. If credentials are loaded
but verification still returns 503, check the logged error code, Firebase
Authentication user-read permissions, and outbound Google API access.

The local HTTP and HTTPS launch profiles now set `GOOGLE_APPLICATION_CREDENTIALS`
to the existing service-account file in your Documents folder. Starting through
those profiles applies it automatically after a restart. Update that path if you
move the file; a Docker container needs its own mounted credential path.

Chrome can report `Cross-Origin-Opener-Policy policy would block the window.closed
call` while Firebase polls Google's popup. A trace that proceeds to `/Auth/Csrf`
shows that popup sign-in returned and the backend exchange is the failing step.
This warning can occur even when authentication succeeds; see the
[Firebase SDK issue](https://github.com/firebase/firebase-js-sdk/issues/8541).
Do not disable token verification or change the database identity flow to hide it.

### Verification

```powershell
dotnet test tests/LadingSystem.Tests/LadingSystem.Tests.csproj --artifacts-path .artifacts/checks
```

The default tests use a substituted token verifier and an isolated SQLite
database to verify MVC authentication, identity tampering, antiforgery, session
cookies, and logout. To also exercise concurrent inserts and email conflicts
against the configured Supabase table, set `SEAROUTE_LIVE_POSTGRES_TESTS=1`.
That test creates and deletes temporary `Users` rows without changing schema.

Run UI checks against a running, configured app with
`node tests/auth-ui.smoke.mjs`. Set `SEAROUTE_TEST_URL` for a different URL.
`npm run test:firebase` skips live writes unless `SEAROUTE_LIVE_FIREBASE_TESTS=1`
is set. With Admin credentials configured on the running backend, it verifies
signup, login, logout, and token-derived PostgreSQL identity, then deletes its
temporary Firebase account and PostgreSQL row. It reads the ignored `.env`
database settings, or accepts a PostgreSQL URI in `SEAROUTE_TEST_POSTGRES_URL`.

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

Set `SEAROUTE_POSTGRES_CONNECTION_STRING` in the host environment for Compose.
Mount the Admin credential file at runtime and set `GOOGLE_APPLICATION_CREDENTIALS`
to its container path. For example, a local Compose override can add:

```yaml
services:
  lading-system:
    environment:
      GOOGLE_APPLICATION_CREDENTIALS: /run/secrets/searoute-admin.json
    volumes:
      - "${SEAROUTE_FIREBASE_CREDENTIALS_FILE}:/run/secrets/searoute-admin.json:ro"
```

Set `SEAROUTE_FIREBASE_CREDENTIALS_FILE` to the host's credential path and include
that override with `docker compose -f compose.yaml -f compose.local.yaml up --build`.
Ensure the mounted file is readable by the container's `app` user. Credentials
must not be copied into the Docker image.
