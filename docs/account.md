# Sign-in, account access and settings

Part of the L2Tracker Companion's developer notes (see the README).

**Auth:** the app opens on a **sign-in screen** and nothing else is reachable until a token validates – paste the token you copy with the website's **Token** button (in the site's header, or Settings → Companion App); it is the website's JWT, valid 14 days, kept by the browser under `l2_jwt_token`. It is stored DPAPI-encrypted (`%LOCALAPPDATA%\L2TrackerCompanion\auth.bin`, current-user scope) only after `GET /api/me` and then `GET /api/characters` succeed. A token the server actually *rejects* (401/403) is deleted, not left on disk; a call that never got an answer (offline, DNS, timeout, a 5xx from the edge) keeps it, so a network blip cannot sign you out – the gate then offers **Retry with stored token** instead of making you fetch the JWT again. Pressing **Sign in** on an empty box is likewise treated as a slip and keeps it – only **Sign out** and a real rejection remove the file. Default API base is `https://l2tracker.cc`, overridden only by `%LOCALAPPDATA%\L2TrackerCompanion\api-base-url.txt` when that file exists – there is no in-app control that shows or edits it.

**Desktop access gate:** `/api/me` reports `desktopAppEnabled` for the account. When it is `false` the app refuses to sign in ("Desktop access is not enabled for this account.") and stores nothing – the same account keeps full use of the website. The flag lives on the server, not in the JWT, so revoking it takes effect here rather than whenever the user's 14-day token expires. `SignInAsync` and `TryRestoreAsync` both go through `ValidateAndStoreAsync`, so a revoked account is stopped on the next app launch too, not only on the next paste. There is no fallback for a backend without `/api/me` – deploy the backend first. The WPF app sends `X-L2-Client: companion/<version>` on that `GET /api/me` only (sign-in and each later launch); the server records one launch per such call. Other API calls omit the header. A build that does not send it is not recorded.

**Sign-in gate:** the token form is the first and only view at startup. The Session and Settings tabs are hidden until `GET /api/me` + `GET /api/characters` accept the token, and the app drops straight back to this screen on **Sign out** or if the stored token disappears mid-session. Reaching the gate stops tracking (the Stop button goes with the Session tab), and **Sign out** additionally wipes the local session store, so unsaved snapshots cannot be posted to whichever account signs in next – it asks for confirmation first whenever that store holds a savable delta. **Retry with stored token** appears whenever `auth.bin` still exists, and re-runs the same validation without a paste.

**Settings tab:** **Options** (User / Debug) and **Account** (signed-in status, **Sign out**) live here, not on the Session tab – this is the only place the mode is switched. Token entry is on the sign-in screen, not here; the API base URL has no control on either screen (see above). User mode hides capture dumps, parse tools, and session inspect on the Session tab. The choice is stored in `%LOCALAPPDATA%\L2TrackerCompanion\options.txt` (`user` / `debug`).

```bash
./scripts/auth.sh --token '<jwt>'
./scripts/auth.sh --garbage   # must print that nothing is on disk
./scripts/auth.sh --status
```

**Character + spot pickers:** after a valid token, the window lists characters from `GET /api/characters` and, on character change, spots from `GET /api/spots?characterId=`. Character is required. Spot may be left empty when the location is stable (see *Spot follow* in `behavior.md`). `% Bonus` prefills from `GET /api/settings` (`defaultBonus`; lamp values and `defaultMinutes` are ignored). Live rates use that same GET's `rateUnit` (`hour` or `minute`). If the GET fails, bonus is schema default 25 and rates are schema default `hour`, with a hint saying why. Session minutes on Save come from the Play Report's own duration, not from a form field and not from the wall clock (see *Save session* in `behavior.md`). Headless:

```bash
./scripts/auth.sh --spots
```

**Native HTTP smoke:** `HttpClient` GET against production with `Authorization: Bearer` and **no** `Origin` header. Confirms CORS/auth middleware does not reject a desktop client. Uses the stored JWT:

```bash
./scripts/auth.sh --http-smoke
```

Must print `HTTP 200` and `JSON: yes` for `/api/characters` and `/api/settings`.
