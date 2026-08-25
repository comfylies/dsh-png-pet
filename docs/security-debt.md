# Security debt

## Accepted: local DSH Web session selector projection

The local DSH Web settings page directly uses session IDs and display titles to support the existing session-list selector. This is an accepted temporary trade-off because those values remain in the user-controlled local Web client and are required to select the target session.

No session bodies, working directories, credentials, prompts, model responses, tool parameters, or file paths may be read, retained, logged, or sent by this projection.

**Closure condition:** when upstream provides a usable third-party Remote projection, restore the Host-side security alias projection and remove direct session ID/title handling from the Web client.
