# Demo User

Disabled by default. When enabled and the panel restarts, this add-on creates the demo account described in `settings.json` (`demo` / `demo` by default).

While you are signed in as that account, the add-on's browser script renders two simulated servers on the dashboard. Their statuses rotate and the console streams fake output — all in the browser. Nothing is written to the panel's nodes, eggs, allocations or servers, so the demo environment never pollutes real data and disappears as soon as you sign in with another account.

The demo account is locked while the add-on is enabled: the panel refuses to edit or delete it.

## Settings

Edit `settings.json` in this folder and restart the panel.

| Key | Default | Meaning |
| --- | --- | --- |
| `username` | `demo` | Demo account username. |
| `password` | `demo` | Demo account password (hashed on creation). |
| `email` | `demo@example.com` | Demo account email. |
| `firstName` / `lastName` | `Demo` / `User` | Demo account name. |

Settings only apply when the account is first created; delete the account (with the add-on disabled) to reseed it.
