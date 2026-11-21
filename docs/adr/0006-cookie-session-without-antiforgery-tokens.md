# The React app uses an Identity cookie session protected by SameSite=Strict instead of antiforgery tokens

Members log in through hand-written `/api/auth/*` endpoints (login, logout, me, password) backed by ASP.NET Identity, which issues an HttpOnly `treasury.auth` cookie. We don't use `MapIdentityApi`, because it is built around email and exposes self sign-up, both of which the spec rules out. Bearer tokens would have to be stored in JavaScript-readable storage; the React app is always served from the same origin as the API (Vite proxy in development, `wwwroot` in production), so a cookie is simpler and safer.

The cookie is `SameSite=Strict` and every state-changing endpoint takes JSON, so a cross-site page can neither send the cookie along nor submit a plain form the API would accept. We therefore don't add antiforgery tokens. Unauthenticated API calls get `401` and forbidden ones `403`, never a redirect; the React app decides where to send the Member.

DataProtection keys, which encrypt the cookie, are stored in the database so that restarting or redeploying the container doesn't log everyone out.

## Consequences

- If the app is ever served from a different origin than the API, or accepts form posts, this decision has to be revisited (antiforgery tokens, CORS).
- Every `/api` endpoint requires a logged-in Member by default (`RequireAuthorization()` on the `/api` group); public endpoints opt out explicitly with `AllowAnonymous()`.
