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

## Panel areas

- `/` — dashboard with the servers assigned to your account
- `/themes` — installed themes; administrators can activate a theme
- `/admin/nodes`, `/admin/servers`, `/admin/users`, `/admin/addons` — administrator-only
  listings, separated from the client dashboard the same way Pterodactyl separates its
  admin area. Addons are visible to administrators only.

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
