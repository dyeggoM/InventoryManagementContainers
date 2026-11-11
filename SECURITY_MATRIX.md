# SECURITY_MATRIX.md

Generated from `.security/security.db`. Do not edit directly.

- Git HEAD: `211a4caa5d574947e4f5bd97c4d1c6df12878fc3`
- Unreviewed scope entries: **0**
- Unresolved findings: **0**
- Integration review: **VALID**

| ID | Severity | Location | Finding | Status |
|---|---|---|---|---|
| SEC-683247C1F1F9 | high | `IMCAPI/Controllers/Controllers.cs::IMCAPI.Controllers.AttachmentController.Upload` | Upload writes the file before the row, so a missing item leaves an orphan and a 500 | verified_fixed |
| SEC-157796AFA9FC | medium | `IMCAPI/DB/Repositories/BaseRepository.cs::IMCAPI.DB.Repositories.BaseRepository.Delete` | The physical-delete branch is unreachable behind the IsActive query filter | verified_fixed |
| SEC-371B48BFC313 | medium | `IMCAPI/Controllers/AuthController.cs::IMCAPI.Controllers.AuthController.Refresh` | Refresh-token reuse is audited but the token family is not revoked | verified_fixed |
| SEC-54DA066706A2 | medium | `IMCAPI/Program.cs::AddRolePolicy` | Auth:Enforce=false silently disables every role check | verified_fixed |
| SEC-CB10DCF5A334 | medium | `IMCAPI/Program.cs::AllowCors` | Development CORS reflects any origin and allows credentials | verified_fixed |
| SEC-E285034FEF63 | medium | `IMCAPI/Controllers/LogsController.cs::IMCAPI.Controllers.LogsController.Logs` | The date route segment is concatenated into a file path without validation | verified_fixed |
| SEC-DAFA55CF12D4 | low | `IMCAPI/Services/AttachmentStorage.cs::IMCAPI.Services.AttachmentStorage.SaveAsync` | The stored extension is taken from the client filename unchecked | verified_fixed |

## Details

### SEC-683247C1F1F9: Upload writes the file before the row, so a missing item leaves an orphan and a 500
- Evidence: IMCAPI/Controllers/Controllers.cs:381 save before :392 add; AttachmentStorage.SaveAsync:67 CreateDirectory
- Description: Upload calls AttachmentStorage.SaveAsync(itemId, ...) at Controllers.cs:381, which writes the bytes to media/items/{itemId}/, and only then _repository.Add(attachment) at :392. An itemId that does not exist passes the <= 0 check, the file is written, and the insert fails on the foreign key: the caller gets 500 and the file stays on disk with no row that names it, so no API route can ever delete it. Reachable by operador and up.
- Source hash: `18c485d26612f81b6f56e381603ef4fb4bf2e4da6dae5b9d4b0cac0a0a8caa77`

### SEC-157796AFA9FC: The physical-delete branch is unreachable behind the IsActive query filter
- Evidence: IMCAPI/DB/Repositories/BaseRepository.cs:164-178 GetById then IsActive branch; query filter in ApplicationContext
- Description: Delete loads the row through GetById, which is subject to HasQueryFilter(IsActive), so an inactive row comes back null and the method returns before reaching the _entities.Remove branch. Soft-deleted Constants and Attachments can never be physically removed or revived, and DeleteRange has the same shape.
- Source hash: `d52da9d75b06da247963fe4b923d6821ef9028d90a2684b8a2443bc14f37905c`

### SEC-371B48BFC313: Refresh-token reuse is audited but the token family is not revoked
- Evidence: IMCAPI/Controllers/AuthController.cs:90-98 audit RefreshReuse then DeleteRefreshCookie+401, no revocation
- Description: When a retired refresh token is presented again the handler writes a RefreshReuse audit row and returns 401, but it does not revoke the replacement token that is still live. A stolen cookie keeps rotating a working session while the audit trail records the theft and nobody acts on it; the standard fix is to kill every token of that user on reuse.
- Source hash: `9adffd009a22687293cf4a0a6de7a04709d16347981cce030343cd699e5a7d10`

### SEC-54DA066706A2: Auth:Enforce=false silently disables every role check
- Evidence: IMCAPI/Program.cs:229-236 (!enforce || roles.Any(context.User.IsInRole))
- Description: AddRolePolicy short-circuits to true when enforce is false: `(!impersonating || allowRead) && (!enforce || roles.Any(IsInRole))`. One configuration value, settable by environment variable, turns all four policies into no-ops with no startup warning and no test that production runs with it on, so a misconfigured deployment authenticates without authorizing.
- Source hash: `716741f863fa50f7485d8febd20ddf129645575aa54da85d4785132858062c7e`

### SEC-CB10DCF5A334: Development CORS reflects any origin and allows credentials
- Evidence: IMCAPI/Program.cs:31 SetIsOriginAllowed(_ => true); :45 AllowCredentials()
- Description: In Development the policy calls SetIsOriginAllowed(_ => true) and then AllowCredentials() for every environment. Any page the developer's browser loads can therefore issue credentialed cross-origin calls to the dev API; the refresh cookie is SameSite=Lax, which limits the practical impact, but the default is a reflection of an arbitrary Origin and should be an explicit list even locally.
- Source hash: `716741f863fa50f7485d8febd20ddf129645575aa54da85d4785132858062c7e`

### SEC-E285034FEF63: The date route segment is concatenated into a file path without validation
- Evidence: IMCAPI/Controllers/LogsController.cs:31-35 fileName interpolation and Path.Combine with no TryParseExact
- Description: Logs builds the file name as $"{date}_Logs.txt" and Path.Combine's it onto the Logs directory with no format check and no containment test. A segment containing '..' or an encoded separator reaches outside the folder on hosts that treat it as one, and the response body is the file content. The route needs TryParseExact plus a check that the resolved path is still inside the Logs directory.
- Source hash: `5c49494478679877cf52ec474f9da914d5b8d637e4da1466b09f3026d656d56c`

### SEC-DAFA55CF12D4: The stored extension is taken from the client filename unchecked
- Evidence: IMCAPI/Services/AttachmentStorage.cs:69-75 extension from fileName, written as-is
- Description: SaveAsync derives the extension from the supplied file name with Path.GetExtension and keeps it whenever its length is 1..10, so .html, .svg or .php are stored verbatim under the media root. The API streams files through a controller today, so this is latent; it becomes stored XSS the day that folder is served statically.
- Source hash: `d03be0713031e593a0cddbba5d154dae1f6ac921f9cd4cc5865fa0c6ed6afb88`

