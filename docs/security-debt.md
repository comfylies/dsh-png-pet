# Security debt

## Accepted: local DSH Web session selector projection

The local DSH Web settings page directly uses session IDs and display titles to support the existing session-list selector. This is an accepted temporary trade-off because those values remain in the user-controlled local Web client and are required to select the target session.

No session bodies, working directories, credentials, prompts, model responses, tool parameters, or file paths may be read, retained, logged, or sent by this projection.

**Closure condition:** when upstream provides a usable third-party Remote projection, restore the Host-side security alias projection and remove direct session ID/title handling from the Web client.

## Known compatibility limit: stock DSH Web settings namespace allowlist

Some stock DSH Web profiles do not expose third-party settings namespaces through the settings API. In that case, this page remains visible but reports its fixed unavailable/write error and cannot persist `dsh-png-pet` settings. The plugin must not work around this with an HTTP endpoint or a custom Remote.

This is an installation compatibility limitation, not a security issue. Installation verification must confirm that the `dsh-png-pet` namespace is exposed to the Web settings client; the closure condition is an upstream exposure opt-in or allowlist support for third-party settings namespaces.
