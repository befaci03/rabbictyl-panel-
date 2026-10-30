# RabbiCtyl

A Pterodactyl-inspired game server panel built with ASP.NET Core (.NET 10), Razor views and
pluggable persistence (SQLite, MySQL/MariaDB, PostgreSQL, MongoDB, Cassandra).

## Requirements

- .NET SDK 10.0.403 or newer (`global.json` pins the feature band)
- [Bun](https://bun.sh) 1.x

## Run locally

```bash
cp config/panel.example.toml config/panel.toml
export RABBICTYL_BOOTSTRAP_ADMIN_PASSWORD='change-me-please'

cd frontend
bun install
bun run build
cd ..

dotnet run --project src/RabbiCtyl.Web
```

The panel listens on <http://localhost:8080>. `config/panel.toml` is resolved from the
content root; set `RABBICTYL_CONFIG` to point somewhere else. Every option also has an
environment override (see `src/RabbiCtyl.Configuration/PanelEnvironmentOverrides.cs`).

## First run: bootstrap administrator

On an empty user table the panel creates a temporary administrator from the `[bootstrap]`
section in `config/panel.toml`. Set the password with the environment variable rather than
writing it to disk:

```bash
export RABBICTYL_BOOTSTRAP_ADMIN_PASSWORD='change-me-please'
```

The defaults are username `admin` and email `admin@example.com`; change them in
`config/panel.toml` if you want different bootstrap credentials.

1. Start the panel and sign in with the bootstrap username (or email) and password.
2. Instead of the dashboard, the panel opens `/auth/setup` and asks you to create your own
   administrator account (name, username, email and password).
3. Submitting the form creates that account, deletes the bootstrap administrator, signs you
   out and returns you to the login page.
4. Sign in with the credentials you just created. The bootstrap account no longer exists.

The bootstrap username and email cannot be reused for the new account, so the temporary
account is always fully removed. If the user table is not empty, no bootstrap account is
created and this flow never runs.

## Sign-in protection

The sign-in form can require a human check. Pick the provider in `config/panel.toml`:

```toml
[security.captcha]
provider = "turnstile"   # none | turnstile | hcaptcha
site_key = "..."
secret_key = "..."
```

Both Cloudflare Turnstile and hCaptcha are supported; the panel verifies the token
server side before it checks credentials. Set `provider = "none"` to disable it.
Registration is off by default; set `allow_registration = true` under `[security]` to let
visitors create their own account. The same captcha protects the registration form.

## Email (SMTP)

Password resets need outgoing mail. Configure SMTP in `config/panel.toml`:

```toml
[mail]
enabled = true
host = "smtp.example.com"
port = 587
encryption = "starttls"   # none | starttls | ssl
username = "panel"
password = "..."
from_address = "panel@example.com"
from_name = "RabbiCtyl"
timeout_seconds = 15
```

With mail enabled, `/auth/forgot` sends a one-hour reset link; the token is stored as a
SHA-256 hash and cleared once it is used. With mail disabled the page tells the user to
ask an administrator, and nothing is sent.

## Panel areas

- `/` — dashboard with the servers assigned to your account
- `/server/{uuid}` — console, files, backups, CRON, users, startup and settings for a server
- `/account` — password, SFTP public keys and profile
- `/themes` — installed themes; administrators can activate a theme
- `/admin/nodes`, `/admin/servers`, `/admin/users`, `/admin/games`, `/admin/addons` —
  administrator-only listings, separated from the client dashboard the same way
  Pterodactyl separates its admin area. Addons are visible to administrators only.

## Nests and game templates

Nests are TOML files in `nests/`. Every `[[egg]]` entry becomes a game template:

```toml
id = "minecraft"
name = "Minecraft"
author = "RabbiCtyl"

[[egg]]
id = "java"
name = "Minecraft Java"
docker_image = "ghcr.io/pterodactyl/yolks:java_21"
startup = "java -Xmx{{SERVER_MEMORY}}M -jar {{SERVER_JARFILE}}"
done = ['regex:.*Done .*']
stop_value = "stop"
script_install = "..."

[egg.variables]
SERVER_JARFILE = "server.jar"
```

`done` is the list of console patterns that mark a server as running. Leave the list
empty and pats marks the server running as soon as it boots, skipping the busy state.
Nest templates are read-only in the panel; edit the TOML file. `/admin/games` also
lets administrators create panel templates in the database, and server creation
copies the selected template's startup, image, process configuration, install script
and default environment onto the server.

Application API: `GET /api/application/nests`, `GET /api/application/nests/{id}` and
`GET/POST /api/application/eggs`, `GET/PATCH/DELETE /api/application/eggs/{uuid}`.
API-created templates are editable; nest templates return `409` on write attempts.

## Status overview

`/overview` groups every visible server into running, busy, offline and failed. The
state is read from pats with a short timeout and falls back to the stored status when
a node is unreachable. Each card has a **View** button that opens a dialog with the
servers in that state; administrators see all servers, other users see their own.

## Database version and maintenance page

The `_state_` table holds a single row with the schema `version` the database was
created with. On startup the panel compares it with the version it expects:

- equal → the panel opens normally;
- older → the console prints `migration in progress of your database...`, the schema
  is upgraded and the website answers with a maintenance page showing both versions,
  the phase and an ETA (the page refreshes itself);
- newer → the panel refuses to run and the maintenance page explains that the panel
  must be upgraded first, instead of letting an old build touch a newer schema.

## OAuth2 sign-in

OAuth2 is a built-in feature exposed through the `oauth2` add-on, which is enabled by
default and has no browser script: the whole flow runs server side. Manage providers
under `/admin/oauth2` (add, edit, remove, enable/disable). Each provider needs the
authorize, token and user-info URLs plus the JSON fields to read the id, email and
name from. Register this callback URL with the provider:

```text
{app.url}/auth/oauth/{provider-id}/callback
```

The client secret is stored in `storage/oauth2.json`. Users are matched by email;
when registration is disabled only existing panel accounts can sign in. Disabling the
`oauth2` add-on hides the provider buttons and rejects the flow.

## SFTP

The SFTP server is part of pats; the panel authenticates it through
`POST /api/remote/sftp/auth`. Users sign in as `<username>.<first 8 characters of the
server UUID>` with their panel password, or with an SSH key stored in `/account`.
Suspended servers and servers on another node are refused.

## Checks

```bash
dotnet build RabbiCtyl.sln
dotnet test RabbiCtyl.sln
cd frontend && bun run typecheck && bun run build
```

CI runs the same commands in `.github/workflows/ci.yml`.

## Themes

Three themes ship in `themes/`:

| Folder | Name | Notes |
| --- | --- | --- |
| `default_dark` | Default Dark | Default theme (`themes.active`) |
| `default_light` | Default Light | Light variant |
| `pterodactyl_blue` | Pterodactyl Blue | Sea-blue/teal variant |

A theme is a folder whose name matches its `manifest.json` `id`:

```text
themes/my_theme/
  manifest.json
  styles/theme.scss          # .css, .scss and .sass are linked
  styles/animations.css      # keyframes stay in plain CSS
  scripts/theme.ts           # TypeScript only; .js is rejected
  assets/mark.svg
```

```json
{
  "id": "my_theme",
  "name": "My Theme",
  "version": "1.0.0",
  "styles": ["styles/theme.scss", "styles/animations.css"],
  "scripts": ["scripts/theme.ts"],
  "assets": { "logo": "assets/mark.svg" }
}
```

`bun run build` compiles every theme into `src/RabbiCtyl.Web/wwwroot/themes/<id>/`,
so `styles/theme.scss` is linked as `/themes/my_theme/styles/theme.css`. Assets are
addressed by id and served from `/api/themes/{id}/assets/{assetId}`, which keeps
templates independent of file paths.

A theme can select a different shell through its manifest. `"layout": "pterodactyl"`
uses the Pterodactyl-style main and auth layouts; omit the field to use the default
layouts. The bundled `pterodactyl_blue` theme uses this option.

## Localization

UI strings live in `src/RabbiCtyl.Web/Localization/<culture>.json` (`en` and `fr` ship
by default). The panel negotiates the culture from the query string, the
`.AspNetCore.Culture` cookie or the browser's `Accept-Language` header; the language
selector in the top bar persists the choice in the cookie. Add another culture by
adding a JSON file and including it in the supported cultures in `Program.cs`.

API:

- `GET /api/themes` — installed themes with their resolved style, script and asset URLs
- `GET /api/themes/{id}` — one theme
- `GET /api/themes/{id}/assets/{assetId}` — an asset by id
- `POST /api/themes/{id}/activate` — admin only; persisted to `storage/theme.json`

## Addons

Addons use the same folder contract but may only link TypeScript scripts (no styles):

```text
addons/example/
  manifest.json
  scripts/main.ts
  assets/icon.svg
```

```json
{
  "id": "example",
  "name": "Example Addon",
  "version": "1.0.0",
  "enabled": true,
  "scripts": ["scripts/main.ts"],
  "assets": { "icon": "assets/icon.svg" }
}
```

An optional C# plugin can be declared with `"assembly": "Example.dll"` and a fully
qualified `"entry"` type implementing `RabbiCtyl.Addons.Abstractions.IAddon`. Enabled
assemblies are loaded into collectible load contexts at startup.

API: `GET /api/addons`, `GET /api/addons/{id}` and
`GET /api/addons/{id}/assets/{assetId}`.

## Realtime

SignalR is exposed at `/hubs/realtime` with the **MessagePack** hub protocol. The browser
client (`frontend/src/ts/realtime.ts`) connects over WebSockets using
`@microsoft/signalr-protocol-msgpack`; the cookie session authenticates it and the
`RealtimeHub` groups server subscriptions by `server:{id}`.

## Project layout

```text
src/RabbiCtyl.Core              entities, store interfaces, manifests
src/RabbiCtyl.Configuration     TOML options and environment overrides
src/RabbiCtyl.Infrastructure    EF Core / Mongo / Cassandra stores, catalogs, addon host
src/RabbiCtyl.Web               Razor UI, APIs, SignalR hub
src/RabbiCtyl.Addons.Abstractions  IAddon contract for C# addons
frontend                        SCSS/TypeScript build for the shell, themes and addons
themes, addons                  folder-based extensions
tests/RabbiCtyl.Tests           xUnit tests
```
