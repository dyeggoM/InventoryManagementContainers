# Changelog

Every pull request, what it changed, and the commits that carried it. New entries go on top under
`Pending`; when the PR merges, the heading becomes its number and date.

The feature list these add up to is in [FEATURES.md](FEATURES.md); what is still planned is in
[ROADMAP.md](ROADMAP.md).

## Pending — feat/catalogue-codes

**La aplicación habla español.** Toda la interfaz está en español: el inventario, el editor de ítem, los
conjuntos, la configuración y las cinco pestañas del panel. Un glosario fijo —Ítem, Conjunto, Pieza, Atributo,
Constante, Grupo— para que cada pantalla no invente su palabra, y `<html lang="es">` con las pocas cadenas que
siguen en inglés marcadas como tales, para que un lector de pantalla no cambie de idioma a mitad de frase. Las
fechas se leen en formato español (día primero, reloj de 24 h con dos dígitos) aunque el navegador esté en otro
idioma: `20/11/2026, 02:05:27`.

**La clave de un atributo dejó de ser su etiqueta.** `Items.Attributes` guardaba `{"Brand": "Bosch"}` y un
`Type` guardaba `"Part"`: la etiqueta *era* el dato, así que traducirla habría sido migrar el JSON de cada ítem —
y volver a migrarlo el día del inglés. Ahora `Constants` tiene **`Code`** (identidad, no se traduce) y
**`Label`** (texto, se traduce): el ítem sigue guardando `Brand` y `Part`, y la pantalla dice «Marca» y «Pieza».
El relleno es `Code := Label`, así que **no se tocó ni una fila de `Items`**.

**El catálogo está traducido y sigue siendo el mismo dato.** Los ocho atributos, los seis tipos y las diez
categorías de primer nivel tienen etiqueta en español y código intacto; las marcas y el vocabulario compartido
(`FieldType`, `AttachmentType`) no se tocan. Renombrar una etiqueta desde Configuración cambia lo que se ve y no
rompe ningún ítem: hay un test que lo prueba renombrando `Marca`.

**Escribir un ítem ahora valida contra el catálogo.** Una clave de atributo que no es un código del grupo
`Attribute` —o una de las cuatro claves que escribe el importador— y un valor de lista que no es un código de su
grupo devuelven `400` con el código en el mensaje, antes de escribir nada. La API responde siempre códigos; traducir
es cosa de la interfaz, que ya tiene el catálogo.

**El import resuelve por etiqueta o por código.** Un fichero histórico que dice `CASES` sigue encontrando
«Estuches», la cabecera de marca se acepta en cualquiera de las dos formas y se guarda siempre como código, y la
subcategoría se liga a su categoría por `ParentId` en vez de por el texto del grupo, que era lo que se rompía al
traducir. El preview sigue enseñando lo que dice el fichero.

**El catálogo es la única puerta.** Una auditoría independiente encontró que la regla se cumplía en un camino y
no en otro, y las dos se cerraron: el import y las correcciones del preview ya no escriben una marca que el
catálogo no conoce —la fila sale roja con el motivo, como con la profundidad—, el validador guarda el **código**
aunque el valor llegue por etiqueta y compara como el importador, y `PUT` de un ítem sin `attributes` conserva los
que tenía en vez de borrarlos. `Activate` valida antes de restaurar.

**Borrar una constante en uso ya no es una trampa.** Antes, quitar una marca que algún ítem usaba dejaba esos
ítems sin poder editarse ni restaurarse, con el código y la etiqueta ocupados por la fila inactiva y sin ruta para
reactivar: un callejón sin salida por un gesto de limpieza. Ahora responde `409` diciendo cuántos ítems y cuántos
archivados la usan. Dos altas simultáneas con el mismo código y una etiqueta repetida devuelven `409`, no `500`.

**Tests:** 323 API + 288 UI. `typecheck`, `lint:check` y `build` verdes; `modern-web-guidance legacy-scan` en cero.

## Pending — fix/web-a11y-platform

**Fixed — dark mode now reaches the browser's own surfaces, not just the page.** The document declared no
`color-scheme`, so in dark mode the native scrollbars, the browser controls and the canvas stayed light, and
the first paint flashed white before the theme was applied. `index.html` now declares
`<meta name="color-scheme" content="light dark">`, and `:root` carries `color-scheme`, switched by
`html:has(body.body--dark)` so it follows the mode that is actually painted — including `auto`, where the
operating system decides. Measured: with the system set to light and the app forced to dark, the computed
`color-scheme` is `dark` and the scrollbar track samples `#2c2c2c` instead of `#fcfcfc`.

**Fixed — the document says what language it is in.** `<html>` had no `lang`, so a screen reader pronounced
the interface with the default language's phonetics. The shell is English; every surface that speaks Spanish —
the import, audit, archive, users and impersonation panels, the file viewer, the impersonation banner, the login
screen and the account menu — now carries `lang="es"` on the element that holds the text, and English strings
sitting inside those surfaces declare `lang="en"` in turn. No wording changed: the app still speaks two
languages, and choosing one is a product decision left for its own round.

**Fixed — wide content can be reached without a pointer.** The spreadsheet preview keeps its real `<table>`, but
its scroll wrapper now carries `tabindex="0"`, `role="region"` and a name: at 390 px it used to overflow with no
way to scroll it from the keyboard. Measured: `scrollLeft` moves 0 → 720 with the arrow keys. The same treatment
went to the audit JSON viewer (a long document in two `<pre>`) and the attachment preview, which had no
focusable child at all when showing an image.

**Fixed — every control in the item editor has a name.** The attribute editor is a form laid out like a table:
the value field of each row is now announced with its attribute («Brand», «Notes»), the name field and the
filter carry their own labels, and the visual header row is `aria-hidden` instead of pretending to be a header
cell. The attachment type selector, which had no label at all, now says what it selects. No placeholder is a
field's only name anywhere in `imcui/src` any more.

**Changed — four sorts stopped copying the array to sort it.** `[...list].sort()` became `toSorted()` where the
project already compiles for it (`lib: esnext`, Node ≥22), so the original list is no longer cloned for
nothing and the intent is explicit.

**Removed — `EssentialLink.vue`.** The leftover template component was referenced by nothing and deleted after
grepping every import and route.

**Tests:** 277 UI tests; `typecheck`, `lint:check` and `build` green; `modern-web-guidance legacy-scan` on
`imcui` reports zero findings.

## Pending — feat/item-sets

**Added — a set is an item with pieces under it.** A drum kit is one record with its kick, snare and toms
hanging off it, so the inventory shows *DRUM SET MAPEX ARMORY · 3* instead of three unrelated drums. The
tree already allowed parent and child; what was missing was a way to say what an item *is*, and a way to
attach pieces without editing them one at a time.

**Added — the vocabulary of kinds.** `Constants` group `Type` gains **`Set`** and **`Accessory`** beside the
existing `Instrument`, `Component`, `Part` and `SubPart`. `Part` is part of the equipment and `Accessory` is
not — a strap or a case hangs off the guitar without being one of its parts — and the distinction is
deliberate in the code, not decoration.

**Added — `POST /api/item/{id}/children` and `DELETE /api/item/{id}/children/{childId}`.** A set takes its
pieces in one call. The move is all-or-nothing: an id from another organization, a repeated id or one that
would close a cycle answers `400` and moves none; another organization's parent is `404` and not `403`, so
its existence is not observable. Detaching does not delete: the piece becomes a top-level item again, which
is why it never leaves the inventory.

**Changed — the inventory gives a set its own affordance.** *New set* creates the item with `Type = Set`
in the same request and opens it; the set's detail gains a **Pieces** section that lists its children and
offers *Add pieces*, which only proposes items without a parent — attaching one that already belongs to a
set would silently move it out of that set. Moving a piece records the actor that did it, like any other
edit.

**Changed — the vocabulary no longer needs a migration that guesses.** The `Type` migration leaves an
unseeded database alone and the startup seed fills it, so a fresh database and an existing one reach the
same place without provisional rows to clean up.

**Fixed — the hierarchy survives concurrency.** Creating, re-parenting, attaching and deleting an item now take
one write lock, so two people rearranging sets cannot build a cycle or push the tree past its depth limit; a
move validates and writes under the same lock, and a delete counts its children there. A delete that loses the
race answers `409 Item has children` instead of surfacing a database foreign-key error as a `500`. Every known
write path is listed in a test that fails if a new one skips the lock; `DetachChild`, `Activate` and the
importer are in it too, and the importer now refuses rows that would exceed the maximum depth instead of
silently building a tree the API would reject.

**Fixed — restoring an item no longer hides a broken hierarchy.** `POST` and `PUT` already refused a parent
chain that contains a cycle; the restore path now answers the same `400` instead of bringing an item back under
a node the tree does not paint. And `GET /api/item` serializes with a depth tied to the tree limit, so a deep
tree returns valid JSON instead of a `200` with half a body.

**Changed — the busy-timeout comment stopped promising what it did not do.** The 500 ms pragma never bounded
the wait: the SQLite provider retries internally for 30 s. Contention is now bounded per attempt by the
connection timeout and covered by an explicit retry, and the code says that instead of implying otherwise.

**Tests:** 302 API tests (12 new: atomic moves, cycles, another organization's ids, detach of a piece that
is not under that parent, `lector` `403`, the two kinds in every organization) and 274 UI tests (19 new:
the picker offers only roots, the set's creation writes its kind, pieces and count come from the reloaded
tree). `typecheck`, `lint:check` and `build` green.

## Pending — feat/organizations

**Added — the inventory is split by organization, on one database.** `Persona`, `Live Room` and `Alquimia`
are rows of `Constants` (group `Organization`, ids `900001`–`900003`), and every table that holds inventory
data — `Items`, `Constants`, `Attachments`, `ItemsArchive`, `AuditLogs`, `ImpersonationSessions`,
`RefreshTokens` — carries a `TenantId`. The organization is read from an `org` claim in the token and applied
as an EF global query filter, so a query that forgets it sees nothing instead of seeing everything.
`TenantId = 0` means *shared*: the organization list itself and the application's vocabulary (`FieldType`,
`AttachmentType`), which is readable from every organization and writable only by `superadmin`. The migration
assigns the existing rows to `Persona` and copies the configuration to the other two in two passes, so the
copies hang from their own parents.

**Added — `POST /api/auth/switch-organization`, `GET /api/organization`.** Switching validates an active
organization, rotates the refresh, carries the new organization through the next refresh, refuses a borrowed
(impersonated) token, and writes an `OrganizationSwitch` row in both the organization being left and the one
being entered. `admin` and `superadmin` may switch; `lector` is refused. Roles are global today, so switching
also grants writes — per-organization read-only is what a membership table would buy, and it is on the
roadmap, not pretended here.

**Added — an import names its destination.** `POST /api/import/preview` and `POST /api/import` take a required
`organizationId`, which may be a different organization from the token's; the placement ladder, the similarity
proposals and the unique-code check all run against the destination's configuration, and the import leaves an
`ItemImport` row in **both** organizations. A missing field is `400`, an unknown or inactive organization is
`400`, and only `admin`/`superadmin` may import.

**Security — the boundary was audited four times, and the audit changed the design.** The independent review
found and we closed: any authenticated user could switch into any organization (now `admin`+); the shared
vocabulary was writable from an organization; any admin could mint an organization row; identity audit rows
(`LoginFailed`, `RefreshReuse`) were written at tenant `0` and no reader could see them; a constant could be
hung from another organization's parent; the import ignored the `organizationId` it documented; a
cross-organization `PUT` answered `204` while writing nothing; the audit reader rejected the very action names
this change introduced; `PUT` could relabel a row into `group = "Organization"`; an attachment `PUT` accepted
an `ItemId` it never validated; the interceptor could leave a transaction open when a guard threw; and the
switch persisted its rotated token only as a side effect of the audit write. `Testing/OrganizationIsolationTests.cs`
and its companions walk every read path — tree, item by id, archive, attachments, constants, and the
`IgnoreQueryFilters` sites — as one organization trying to see another.

**Fixed — an item `PUT` no longer writes files it will not record.** `PUT /api/item/{id}` accepted an attachment with `uploadBase64`, wrote the file and inserted no row, because `UpdateAsync` copies scalar properties and the `Attachments` navigation is dropped: repeatable, unnameable media on disk. The field is now refused with `400` before any write and attachments go through `/api/attachment`. Found by the fourth audit, pre-existing.

**Tests:** 235 API tests, and a fresh database migrated from scratch is checked by
`cache/scratch/verify_tenants.py`: three organizations, 238 constants each, no cross-organization parent, no
inventory row without an organization.

## Pending — `feat/import-similarity`

**Changed — `npm run lint:check` checks formatting instead of printing it.** The script ran
`prettier "…"` with no `--check`, so it formatted to stdout and exited `0` on every file: the CI step
that was supposed to catch a badly formatted file could never fail. It passes `--check` now, and the 31
files that had drifted are formatted — the only change in them is whitespace and quotes.

**Added — when the file writes a name the configuration almost has, the import proposes it instead of
filing it under `Other`.** The placement ladder (exact → alias → value is a category → whole-word
completes → plurals → truncates) is unchanged; a Jaro-Winkler score now runs only where every rung failed,
proposes the closest configured value when it scores **≥ 0.80** and beats the runner-up by **≥ 0.02**, and
**places the row there** in yellow — "se puede importar, conviene revisar" — with the percentage in the
group's note and a structured `suggestion` in the preview (`{ label, category, percent }`). Green still
means the configuration spells it exactly that way, and nothing is written to `Constants`.

A proposal is a recommendation attached to a destination, not a silent rename: the row is importable
where it now sits, and the note says why. A word guard refuses candidates whose first word differs and
which neither contains the other, because that is what stops `ATRIL CONGA` from becoming `CONGA` at 83 %
— an atril is a stand, not a drum — and what keeps `BODEGA → BODYPACK` (81 %), `CASE → CRASH` (81 %) and
`GUITARRA FENDER → GUITARRA ELECTRICA` (89 %) out of the file.

On the owner's spreadsheet, against a clean database: `Other` drops from **120 rows in 50 groups to 112 in
42** — `CONGA02`, `CONGA03`, `TUMBADORA02`, `TUMBADORA03`, `TUMBA`, `TRIGER`, `BAJO ELECTROACUSTICO` and
`MEDICIONES` land where they belong at 91–99 %. Four of those eight rows are red for "Sin descripción"
and stay excluded until somebody names them. `LUZ` (18 rows), `SET` (10), `BODEGA`, `STAGEBOX`,
`INTERFAZ MADI`, `SERVER`, `SOFTWARE`, `PANTALLA` and the 24 `CASE PARA …` labels get no proposal: they
are concepts the configuration does not have, not typos, and guessing them would file stock in the wrong
place. The preview shows the recommendation as a chip before anything is imported.

## #51 — The dashboard is usable on a phone · merged 2026-11-15

**Fixed — the management screens were cut off at 390 px.** The tab bar held 534 px of tabs in a 356 px
bar (the last tab sat entirely off screen), and the three panels with mobile cards took their
`min-content` width, so one long entity id pushed the row and its action button past the edge with no
scroll to reach them. Below `sm` the import tab reads "Importar" with the full name in `aria-label`,
the tabs are tighter, and the cards carry Quasar's `full-width`. Import and impersonation measured
clean and were left alone; desktop at 1300 px is unchanged.

## #50 — The ledger keeps a month, and the clock enforces it · merged 2026-11-15

**Changed — the delete ledger keeps a month, and now something enforces it.** `Archive:Retention:Days`
had no default, so `POST /api/item/archive/purge` answered `400` unless somebody configured an age or
passed one; the policy existed in a document. The default is now **30 days**, and a daily sweep
(`ArchiveRetentionSweeper`) applies it without anyone calling the endpoint: the rows older than the
cutoff go, the files they were the last owner of go with them, and the purge leaves an `ArchivePurge`
audit row either way — `by: "archive-retention"` for the sweep, the caller's id for the endpoint. The
age resolves in one place for both: request → `ARCHIVE_RETENTION_DAYS` → `Archive:Retention:Days` →
30. Setting `0` turns the sweep off and leaves the endpoint as the only way in, which is what a dev box
may prefer.

## #49 — An accented subcategory finds the configuration entry · merged 2026-11-15

**Fixed — the real inventory parked nine shelves in `Other`.** The file writes `SILLA BATERÍA` and
`Constants` says `SILLA BATERIA`; the importer's fold used `string.Normalize`, which this API cannot
use — `InvariantGlobalization` makes it a no-op at runtime, while the unit tests, in a project without
that setting, kept folding and kept passing. The fold is a table of the diacritics these names use,
the regression test posts a CSV through the endpoint, and the `TECALDO` alias now points at a label
the configuration actually has (`BASE TECLADO SENCILLA`) instead of one it does not. On the real file:
green 230 → 239, `Other` 83 → 74 yellow, import unchanged at 455 records.

## #48 — The test harness stops deciding where the next host connects · merged 2026-11-15

**Changed — the test harness stops deciding where the next host connects.** `ApiFactory` handed its
own connection string and media root to the process as environment variables, so every host built
afterwards inherited them: a second factory pointed at the first one's database, and a host created
for a different purpose migrated a file another test had already migrated ("table AspNetRoles already
exists", seen for real while writing the startup tests). Each host now receives its settings with
`UseSetting`, which is what a value read during startup needs — `ConfigureAppConfiguration` arrives
too late for the JWT key the API reads in `Main`. A test builds two factories and checks the second
one does not see the first one's data, and that the process environment stays untouched.

**Changed — two tests named more than they checked.** `Preview_RequiresAnAdminAndASpreadsheet`
asserted a missing credential and nothing about the role; it is now two tests, and the second one
signs in as a `lector` and gets 403, which is the half the old name was claiming and nobody was
checking. `TrashOperations_RefuseAPathThatEscapesTheMediaRoot` created a file and then skipped the
test when the platform refuses a symlink, leaving the file behind; the symlink is attempted first now.

## #46 — The composition root and the controllers are readable · merged 2026-11-15

**Changed — no behaviour change, and that is the point.** A readability pass over the API and the
suite, with the 161 tests as the only proof that nothing moved underneath:

- `Program.cs`: 389 lines with a 337-line `Main` → **54 lines**, with the composition in
  `IMCAPI/Startup/` (`CorsSetup`, `ApiServices`, `AuthSetup`, `AuthorizationSetup`,
  `RateLimitingSetup`, `BootWarnings`, `ImpersonationSetup`, `StartupTasks`). Same registrations, same
  lifetimes, same middleware order.
- `Controllers.cs` (759 lines, three controllers in it) → `ItemController.cs`,
  `AttachmentController.cs`, `ConstantsController.cs`, bodies unchanged.
- `ItemImporter` (822 lines) lost its middle responsibility: `SubcategoryPlacement` owns the
  configuration map and the placement ladder as a scoped service, and is now testable without HTTP.
- The suite: `Testing/ApiTestBase.cs` owns the factory lifetime, the login plumbing and the eight
  helpers that existed two or three times — 846 lines out, 318 in, 161 tests still green, none removed
  or weakened.

**Added — the parts of the suite that could not see the refactor.** *180 tests* now (19 new):

- `Testing/SubcategoryPlacementTests.cs` — the placement ladder against a hand-built configuration:
  exact spelling, punctuation-blind matching, a name the file shortens, the shortest candidate, a tie
  that keeps the file's own words, singular/plural, an extended name, a value that is itself a
  category, exact-beats-closer, the `Other` fallback and a row with no subcategory. It was only
  reachable through HTTP before.
- `Testing/StartupInvariantsTests.cs` — the host refuses to start outside Development with the
  published development key, with an empty CORS list, or with `Auth:Enforce=false`; the same settings
  are accepted in Development (the control); the login window throttles an unauthenticated caller, so
  rate limiting still sits before authentication; the anonymous probe endpoint answers; and the
  service lifetimes the API depends on are still what they were.

## #43 — The import keeps the configuration's tree · merged 2026-11-15

**Changed — the import builds the configuration's tree, not the spreadsheet's.** A row used to land
under its sheet name (`BACKLINE`, `SONIDO`), which turned the file's layout into invented roots. A
row's parent is now the *Category* that owns its SUB CATEGORIA in `Constants`: `BASE TECLADO SENCILLA`
goes under `Hardware`, `MICROFONOS` under `Sonido`, a case under `Cases`. The subcategory is written
with the configuration's spelling — `CONGA DOBLE` → `BASE CONGA DOBLE`, `MESA` → `MESA PERCUSION`,
`MICROFONO` → `MICROFONOS`, `GTAR COMBO` → `GTR-COMBO`, `CASES` → the category itself — and two sheets
that write the same subcategory now share one group instead of two.

**Changed — a placement says how it was decided.** A row the configuration spells exactly is green; a
row placed by a rule (truncated name, singular/plural, category-as-subcategory) is yellow and carries
the rule in its own reasons, with the group's `note` repeating it. An abbreviation that fits several
subcategories of one category (`BASE`) keeps its own words under that category instead of being read
as the shortest match. A name the configuration does not know lands in `Other`, named as it came,
yellow, saying why — measured on the sample file: `CASE PARA CONGAS` there, `INVENTO NUEVO` too.

**Measured**: `dotnet test` 158 passed. The fixtures' verdicts moved with the rule — one green, five
yellow, three red on the sample workbook — and every count in the tests was re-derived from a real
preview, not adjusted until it passed.

## #42 — The module map is committed with its files · merged 2026-11-15

**Fixed — the module map is committed with the files it points at.** `AI_INVENTORY.md` is a router
index into `inventory/<module>.md`, and those 74 module documents were in `.gitignore`: on GitHub,
and in any fresh clone, every link in the map was dead. They are tracked from now on (676 KB) and the
pre-commit hook, which already staged them, now has something to stage. Six orphan slices left behind
by renamed files went with the regeneration; the index and the directory now agree exactly.

## #41 — A borrowed token cannot become the actor · merged 2026-11-15

**Security — a borrowed token can no longer become the actor.** `POST /api/auth/stop-impersonation`
minted the actor's own token from the `act` claim alone, so anyone holding a borrowed token — seven
minutes of read-only access, handed to support, a demo device or an XSS — could exchange it, with no
cookie and no password, for a 15-minute superadmin token with write. It now also demands proof of the
actor's session: their refresh cookie, hashed and checked against a live, unrevoked token of that
same actor. Measured before: `200` and a superadmin token. After: `403`, and a bare holder is handed
nothing at all.

**Security — a borrow is now a row the server can see and stop.** `ImpersonationSessions` records who
borrowed which role, from when, until when, and how it ended; the token carries its id as `imp_sid`
and every authenticated request checks it is still live. Consequences, all measured: ending a borrow
kills the token on the next request instead of leaving it valid for the rest of its seven minutes; a
borrow can be ended once (a second attempt has no live token to make it with); letting it expire has
the same effect as ending it; and an end still lands — without a token — when the actor's refresh
family has been revoked, which used to leave the borrow with no way out.

**Security — the audit trail names the borrow.** `AuditLogs` gains `ImpersonationSessionId`; the
`ImpersonateStart`/`ImpersonateEnd` pair and any row written while impersonating carry it, so
"what did this borrow touch" is a query instead of a guess. The session's own bookkeeping row stays
out of the audit stream — it is not a business change.

**Security — development stops reflecting any origin with credentials.** The dev CORS policy echoed
whatever `Origin` it was given, with `Allow-Credentials: true`, so a page on the internet could read
this API with the tester's refresh cookie. It now reflects loopback and private addresses only
(RFC 1918, link-local, IPv6 unique-local) plus whatever `CORS:AllowedCors` names; the LAN case — a
phone on the workshop wifi — keeps working, verified both ways.

**And the client sends the session cookie.** The transport only sent credentials on the refresh, so
`Salir` would have answered `403` and quietly skipped `ImpersonateEnd`. Every call carries the cookie
now; the cookie is HttpOnly and scoped to `/api/auth`, so it only ever reaches the auth routes.

## #34 — Stock can be imported from a spreadsheet · merged 2026-11-15

**A spreadsheet can be read before it is imported.** `POST /api/import/preview` (admin) reads an
`.xlsx`/`.xls` and answers the tree it describes — sheet, subcategory, record — with every row marked
green (import it), yellow (importable, but look first: brand outside the catalogue, no code, a
description too short, a corrected subcategory spelling) or red (not importable: no description, or a
code repeated in the file). It writes **nothing**: the import is a separate decision.
`ExcelDataReader` is the only new dependency, and it is used to read.

**The import writes, once you say so.** `POST /api/import` (admin) turns the tree the preview described
into records — sheet → root, SUB CATEGORIA → the level under it, row → a record carrying `Brand`,
`Code`, `Reference`, `Serial`, `Notes` and `Dimensions` in its attributes — in a single transaction.
Green rows always go in, yellow ones unless you untick them (their code travels in `skipCodes`), red
ones never. Running the same file twice creates nothing the second time: a code already in the tree,
or a nameless record that is already there, is skipped instead of duplicated. One `ItemImport` audit
row records the counts.

**And the dashboard shows it.** The *Importar desde Excel* screen takes the file, renders the tree with
its three colours, lets you filter to the doubtful rows and untick the yellow ones, and only then writes
— with the counts of what went in, what was skipped, and why. Below `sm` it is the card layout.

Measured on the owner's file (560 records + 18 section titles across five sheets): 350 green, 105 yellow,
105 red — the reds being rows that carry a code but no name.

## #29 — Restoring a record gives its files back · merged 2026-11-12

**Restoring a record brings its files back.** Deleting used to unlink the attachments, so an archived
record kept the *names* of files nobody could return. Deletion now moves them to `media/trash`, mirroring
the live tree, and `PATCH /api/item/{id}/activate` moves them back and re-creates the attachment rows
from the ledger snapshot — so a restored record has its photos again, byte for byte. The purge is the
only place that now destroys bytes. Headers `X-Restored-Attachments` / `X-Missing-Attachments` say how
many came back; an archive written before this change restores with none, which is the honest answer
rather than a 404 nobody explains.

**The file route no longer trusts the path it reads.** `GET /api/attachment/{id}/file` resolved
`FilePath` straight onto the disk; it now goes through the same confined resolver the trash uses, so a

## #28 — The project is MIT licensed · merged 2026-11-12

**The project is MIT licensed.** `LICENSE` carries the canonical text and AGENTS.md says so, so
"can I use this?" now has a written answer instead of a guess.

## #27 — Archive retention, paging without a per-page COUNT, and the dashboard on a phone · merged 2026-11-11

**The archive can expire, when somebody says so.** `POST /api/item/archive/purge` (superadmin) removes
ledger rows older than the retention age, which comes from `olderThanDays` or from
`Archive:Retention:Days` / `ARCHIVE_RETENTION_DAYS`. It is **off by default on purpose**: with nothing
configured the route answers `400` instead of treating "no policy" as "delete everything". Each run
writes an `ArchivePurge` audit row with the cutoff and the count, and touches neither live items nor the
files on disk.

**A page no longer counts the whole table.** `GET /api/item/archive` and `GET /api/audit` answered
`COUNT(*)` plus `Skip/Take` on every request, which makes page 20 as expensive as page 1. The total is
now answered once, on page 1; later pages leave it out and send `hasMore`, computed by asking the
database for one row more than the page shows. Both panels keep the total they got on the first page and
use `hasMore` to decide whether the next button is live — which also fixes a pager that treated a page
as the whole result whenever the API named the count `totalCount` and the client looked for `total`.

**The dashboard fits a phone.** The three superadmin panels got a card layout below the `sm`
breakpoint — the fields that matter on a card, the actions in a row you can reach with a thumb, filters
stacked full width, pagination at the bottom — and keep their tables at `sm` and above.

Verified: `dotnet test` **105 passed**, `npm run test` **173 passed** (19 files), `npm run typecheck`
and `npm run lint:check` clean.


## #26 — The commit gate, documented · merged 2026-11-11

The commit gate is documented where the next person looks. `AGENTS.md` now has the three commands that
satisfy it, says that a review is consumed by the commit that carries it, and records that the rendered
matrix stays out of Git because it carries the HEAD it was generated for.

Also corrected: `AGENTS.md` still advertised 57 tests; the suite is 101.

## #25 — The file opens in the app, searchable attribute lists, and dark mode · merged 2026-11-11

**Opening an uploaded file works again, inside the app.** The *open original* action was an `<a>`
pointing at `/api/attachment/{id}/file`, so the browser opened a new tab without the bearer token,
the route answered `401` and the tab stayed blank — the thumbnail looked fine because it is a stored
preview, not a request.

- The file is fetched with the session the app already holds (`api.getBlob`, same single-flight
  refresh as every other call) and shown in a **modal** instead of a tab: images render as images,
  PDFs in a viewer, anything else offers *Descargar*. The bytes are requested when the dialog opens,
  not before, and the object URL is revoked on close.
- The route stays behind `RequiereLectura`, which is what a modal can do and a new tab cannot.

**Attribute lists can be searched, and an admin can add a value from the form.**

- The value control is a real input on a phone now: typing filters the options instead of leaving the
  long tap-menu as the only way in.
- When what the user typed is not in the list, `admin` and `superadmin` get *Añadir «X» a «Label»*;
  it creates the constant in the same group, refreshes the catalogue and leaves the row with the new
  value, so a missing value no longer means leaving the form for Configuration. `operador` and
  `lector` never see the affordance, and the API still requires `admin`.
- The new value lands under the catalogue node that owns the group (`Brand` values under the `Brand`
  node), not under the `Attribute` definition that merely names the attribute.

**Dark mode, system by default and switchable.**

- Quasar's `Dark` plugin is installed; `stores/theme-store.ts` keeps `auto | light | dark` in
  `localStorage`, `auto` being the default and what a fresh browser gets. `boot/theme.ts` applies it
  before the app mounts, so a reload in dark does not flash a white page first.
- The header carries the control (icon reflects the mode, menu with *Sistema* / *Claro* / *Oscuro*,
  the active one ticked). `auto` follows the OS and keeps following it.
- The screens that would have broken were repaired rather than left to Quasar's defaults: the tab
  panels had a `bg-white`, the tree dialog a `background: #fff`, and Quasar's colour utilities
  (`text-grey-7`, `bg-orange-1`/`text-orange-10`, `bg-red-1`, `bg-blue-1`) are plain hex, so
  `css/app.scss` re-pairs them in dark mode with measured contrast (6:1 or better on the dark page).

Verified: `npm run test` **161 passed** (18 files), `npm run typecheck` clean, `npm run lint:check`
exit 0. Against the running API: an upload then `GET /api/attachment/{id}/file` with the token answers
`200 image/png` byte-identical (68/68), the same request without a token answers `401`, and a constant
created with the payload the page sends appears in `GET /api/constants/group/Brand` and in the
Configuration tree under the right parent. Dark mode was checked in a real browser at 780×437: the
inventory tree, the dashboard tables (no white panel left), the file preview modal and the login card
all render dark with readable text, and switching back to *Sistema* returns the body to the OS mode.

## #24 — The LAN script stops handing out a password that does not work

`scripts/dev-lan.sh` printed the bootstrap password as if it were the account's login. On any database
that already has a superadmin the seeder ignores that value, so the printed password answered `401`.
It now names the account and says the value only applies to an empty database.

## #23 — Impersonation by role, the security round and the archive screen · merged 2026-11-11

**Impersonation is by role, and only downwards.** The superadmin picks a role, not an account, which
is the rule the app needed to be honest about: nobody signs in "as" a colleague.

- `POST /api/users/impersonate` takes `{ "role": "operador" }` and replaces the account-based route.
  `superadmin` may enter as `admin`, `operador` or `lector`; `admin` as `operador` or `lector`;
  `operador` as `lector`; `lector` may not impersonate at all. `superadmin` is never a valid target,
  and a role at or above the caller's own answers `400`.
- The token keeps the **actor's real id** in `sub` and carries the target role as its only role claim,
  plus `act`, `act_email`, `impersonated_role` and `scope=read`. Seven minutes, and deliberately not
  renewable: the refresh cookie belongs to the caller's own session, so when the borrowed token
  expires the session comes back as itself and the banner clears, instead of the expired token being
  re-installed and bouncing the user to the login form.
- Read-only is enforced in `Program.cs` for every unsafe method, including the archive restore, and an
  impersonation token cannot impersonate again. `ImpersonateStart`/`ImpersonateEnd` are audited with
  the actor taken from the token; no audit row ever names the target as the author.
- The dashboard's impersonation panel is a role picker limited to the roles below the caller, and the
  store refuses a role at or above the caller's own before the request leaves the browser.

**An account created in the app can log in.** It could not before: `CreateUser` made an Identity row
with no password, so every account the dashboard created was unusable.

- `POST /api/users` accepts an optional `password`; without one the server generates a 24-character
  password (one character from each required class, shuffled with `RandomNumberGenerator`) and returns
  it **once** as `initialPassword`. A password the caller chose is never echoed back.
- `POST /api/users/{id}/password` (superadmin, optional body) sets or resets a password, returns the
  generated value once, revokes that account's refresh tokens and writes a `PasswordReset` audit row.
- The interface surfaces both: an optional field in the create dialog, a one-time dialog with the
  generated password, and a *Restablecer contraseña* action per row.

**The audit trail is the archive's companion, and both now have the UI they were missing.**

- New **Archivo** tab: filter by label, deleter and date range, paginate over the server's total,
  and restore a record with confirmation; a row already restored is marked and cannot be restored
  again, and a `404` is reported as "ya fue restaurado" instead of an error screen.
- Restoring a record now obeys the same depth bound as create and update. It used to re-insert the
  row without checking, so a record archived at level 12 could come back at level 13 once its parent
  had been moved deeper — measured today, and closed with the same `AnalyzeParentChain` guard the
  update path uses.

**The login door has a brake.** Until now the only limit on `POST /api/auth/login` was Identity's
lockout, and `refresh`/`logout` had none.

- A fixed window per client address, configured with `Auth:RateLimit:PermitLimit` (default 10) and
  `Auth:RateLimit:WindowSeconds` (default 60), answers `429` once spent. It covers the `/api/auth`
  controller only: reading the inventory is not inside that window.
- Non-browser clients may now send `POST /api/auth/refresh` with a borrowed bearer without the
  impersonation write-block answering `403` — the refresh trades the cookie for the caller's own
  token, which is the whole point of the route.

- Leaving an impersonation now goes through `POST /api/auth/stop-impersonation` instead of swapping
  the token in the browser, so the audit trail records the end of every impersonation, not just the
  ones an API client closed. A refused or unreachable call still returns the caller to their own
  token, or ends the session when there is none — the banner never outlives the borrowed token.

**Security — the seven findings the scanner recorded are closed**
- **A file can no longer be written for a record that does not exist.** `POST /api/attachment/upload`
  checks the record first, and if the insert fails anyway the file it just wrote is removed. It used
  to answer `500` and leave an orphan under `media/items/{id}/` that no route could name or delete.
- **The extension saved to disk is an allow-list.** A client-supplied `.html`, `.svg` or double
  extension is re-detected from the content instead of being kept, which is what stops this from
  becoming stored XSS the day the media folder is served.
- **Reusing a refresh token kills the whole family.** The reuse was audited and then the replacement
  token stayed alive, so a copied cookie kept rotating a working session. Every live token of the
  account is revoked now, and the honest client signs in again.
- **`GET /api/logs/{date}` parses the segment** (`yyyy-MM-dd`) and refuses a path that resolves outside
  the `Logs` directory; an encoded separator or a drive letter never reaches `Path.Combine`.
- **`BaseRepository.Delete` can reach the row it was asked about.** The lookup ran through
  `HasQueryFilter(IsActive)`, so a soft-deleted `Constant` or `Attachment` could never be physically
  removed and the second delete silently did nothing.
- **A control that gets switched off now says so.** Outside Development the API refuses to start when
  `CORS:AllowedCors` is empty (the policy allows credentials, so "any origin" is not a default a
  deployment may take) or when `Auth:Enforce=false`; in Development both log a warning naming the risk.

- **The JSON attachment route got the same two guards** as the multipart one. The independent
  validation found the orphan-file bug alive on `POST /api/attachment` (`uploadBase64`), which is the
  route the form used before the multipart work landed.
- **`GET /api/logs/{date}` reads the files the logger actually writes.** The reader asked for
  `yyyy-MM-dd_Logs.txt` while `FileLoggerProvider` writes `yyyyMMdd_Logs.txt`, so every well-formed
  date answered 404 with the log sitting next to it. Found by the same validation pass.
- **The hard delete no longer 500s on a row that still has children.** A constant with live children
  cannot be removed by the database (`Restrict`), and the unfiltered lookup that made a second delete
  reach the row turned that into a 500 the UI could not explain: the row now stays soft-deleted and
  the call answers `204`, which is what "already deleted" means.

- **The failed-insert cleanup is reachable.** `base.Post` swallows the exception and answers `500`, so
  the cleanup around it never ran and the orphan came back the moment an insert failed. The JSON route
  now does the insert itself, removes the file and then answers `500`; `DeleteRange` got the same
  guard as `Delete`, and the thumbnail is computed before the write so a throw cannot leave a file
  behind either. A test renames the table out from under the insert and asserts the media folder is
  empty afterwards.

Verified: `dotnet test` **101 passed**, including `Testing/SecurityRegressionTests.cs` — nine tests
covering the upload guards on both routes and a forced insert failure, the extension allow-list, the
log path and its file-name mismatch, token-family revocation and the two deletes. Each finding's state
is tracked in `SECURITY_MATRIX.md` and the gate (`security_matrix.py check`) reports it.

**Database debt closed**
- `AuditLogs.Action` goes from 32 to 64 characters and `EntityId` from 512 to 1024. SQLite stores them
  as `TEXT` and never complained, which is exactly why it would have broken only after a move to SQL
  Server — one action name over 32 characters away. The migration records the model change and states
  that the width is metadata-only here.
- `ItemsArchive.Label` gained an index: the archive screen filters by label and the table only grows.

**Fixed**
- A password reset now invalidates the access tokens already issued: the token carries `sstamp` and
  the pipeline compares it with the account's current security stamp, so a reset actually ends the
  session (measured: the old bearer token answers `401` immediately after the reset).
- A role change now reaches the sessions already open: `PUT /api/users/{id}/roles` rotates the
  account's security stamp, so the token minted with the old roles answers `401` instead of carrying
  them until it expires (measured: a promoted `lector` token stops working at once, and the next
  login writes).
- `GET /api/item/archive` computed its page offset in `int`, so a large `page` wrapped and silently
  served the first page; the offset is now computed wide and clamped.
- `npm run lint:check` exited 2 on a clean checkout because its prettier half resolves
  `--ignore-path .gitignore` against `imcui/`, which had none, and walked the built bundle. The
  documented check now passes (`imcui/.gitignore` added, exit `0`).

Verified: `dotnet build IMCAPI/IMCAPI.csproj` 0 errors, `dotnet test` **89 passed**, `npm run test`
**129 passed**, `npm run typecheck` clean, `npm run lint:check` exit `0`.

## #22 — Roadmap notes: impersonation by role, audit widths, refresh dates · merged 2026-11-11

Documentation only. `ROADMAP.md` records the constraints that still have to be honoured before (c),
(d) and (e) close: impersonation is **by role and down the hierarchy**, never as a specific account;
the `AuditLog` column widths have to be reviewed against the fields they store; `ItemsArchive` is the
delete ledger and `AuditLogs` the change history, and they must not duplicate each other; and a
refresh token's dates stay server-side.

## #21 — Deleting a record archives it · merged 2026-11-10

- **Item deletion is now archival**, not soft deletion: `ItemsArchive` stores a detached item snapshot,
  deletion actor/time and attachment metadata; the live item and attachment rows are removed and the
  attachment files are deleted.
- **Superadmin archive listing** supports deletion date, actor, label and pagination filters; admin
  activation restores the latest unrestored snapshot, preferring the original ID when available and
  writing a `Restore` audit action.
- Deletion now returns `409 ProblemDetails` when children remain, preserving their parent links. Item
  hierarchies are cycle-checked and limited to seven levels by default; configure with `Items:MaxDepth`
  or override using `ITEMS_MAX_DEPTH`.
- `IsActive` is server-owned on item create and update. API coverage exercises deletion, restoration,
  filtering, authorization and server-managed activation state.

## #20 — The service worker registers without a lazily installed dependency · merged 2026-11-10

The PWA registration imported `register-service-worker`, which is declared in `src-pwa/package.json`
and installed by Quasar **lazily**, only when it builds PWA mode. Any checkout, worktree or CI run that
type-checks before that first PWA build failed with *Cannot find module 'register-service-worker'*, and
the dev server painted the error over the login screen.

The registration is now written against `navigator.serviceWorker` (typed, no dependency), so the code
that is type-checked is the code that runs, and it is wired through `sourceFiles.pwaRegisterServiceWorker`
instead of sitting in the commented block. The unused `sw-offline` dispatch is gone.

Verified: `npm test` 86 passed, `npm run typecheck` clean, `npm run build` and `npx quasar build -m pwa`
exit 0 with the `sw-updated` event in the bundle.

## #19 — The code map becomes a router index, and using it is a rule · merged 2026-11-10

`AI_INVENTORY.md` went from a single 131 KB document to a **6 KB router index plus 18 module slices**
under `inventory/`, so a change starts by opening the one module it touches instead of walking the
repository. The generator's default split threshold dropped from 160 KB to 40 KB for that reason, and
the "how to use" block is now a mandatory protocol (index first, ranges not files, reuse before
writing, read Impact before a signature change, never paste the map or a file into the prompt).

`AGENTS.md` gains the same rules as a required order of work, plus the ownership map, and the
pre-commit hook is now installed and versioned in `.githooks/` instead of being assumed: it had never
been installed in this clone, which is why the map had gone stale and still listed the routes from
before the users work.

## #18 — Users and impersonation · merged 2026-11-10

The superadmin can now manage accounts and look through another role's eyes without knowing its
password.

- **`UserController`** (`GET`, `POST`, `PUT /{id}/roles`, `DELETE /{id}`) under `RequiereSuperadmin`,
  answering the shape the dashboard already expects; `UserProfile` carries `IsActive` and the last
  access time.
- **Hard rules, verified against a live instance**: a duplicate email answers `409`; the `superadmin`
  role cannot be assigned through the API (`400`); the bootstrap superadmin cannot be deleted (`409`)
  or demoted (`400`); nobody can grant themselves a role (`400`). `DELETE` deactivates the Identity
  row instead of deleting it, so the audit history keeps naming a real account.
- **Impersonation** (`POST /api/users/{id}/impersonate`): a 7-minute access token whose `sub` is the
  impersonated account and whose `act` claims name the superadmin, with `scope=read`. With it, reads
  answer `200`, every write answers `403`, `/api/users` answers `403` and impersonating again answers
  `403`. `POST /api/auth/stop-impersonation` returns a normal token for the actor, which writes again
  (`200`).
- **Audited**: `RoleChange`, `ImpersonateStart` and `ImpersonateEnd` rows, all with the actor taken
  from the token.

Verified: `dotnet build` clean and `dotnet test` **74 passed**; the matrix above was executed against a
fresh instance on port 5210 with its own database.

Not done yet, and it is why the dashboard cannot log in as a freshly created account: **there is no
way to set a password for a new user**. That lands with the next change.

## #17 — Login sends its credentials · merged 2026-11-10

`imcui/src/stores/auth-store.ts` called `POST /api/auth/login` without `credentials: 'include'`, so the
browser discarded the `HttpOnly` refresh cookie and the session never outlived its access token. The
call now sends credentials, and refresh works cross-origin.

## #16 — Who changed what, with the before and after · merged 2026-11-10

The database now records **who changed what, with the before and after**, instead of only a
last-touched-by string the client could forge.

- **`AuditLog` table + migration** (`TimestampUtc`, actor id and email, impersonated user, action,
  entity, entity id, `BeforeJson`, `AfterJson`, IP, user agent, request id) with indexes on
  `(Entity, EntityId)` and `TimestampUtc`.
- **One `SaveChangesInterceptor` is the only writer**, hooked at the persistence layer so no
  controller can skip it. It serialises the changed values before and after, takes the actor from the
  token (never from the body) and the impersonated user from the actor claim when there is one, and
  redacts `PasswordHash`, `SecurityStamp`, tokens and token hashes before anything is stored. Audit
  rows are inserted through parameterised SQL from `SavedChanges`, which is what keeps the interceptor
  from auditing itself; modifying or deleting an audit row through EF throws.
- **`CreatedAt`/`CreatedBy` on the base entity**, written server-side. The client cannot set `Id`,
  `CreatedAt`, `CreatedBy`, `UpdatedAt` or `UpdatedBy` any more: `POST` with `id: 999` and forged
  metadata stores `id: 1` and the authenticated actor (verified).
- **Explicit rows** for login, failed login, logout, reuse of an already-rotated refresh token and
  role changes.
- **Soft deletes are their own action** (`SoftDelete`) so a delete is findable with
  `GET /api/audit?action=SoftDelete` instead of hiding as an `Update`.
- **`GET /api/audit`** with filters (actor, entity, entity id, action, from, to) and pagination,
  `RequiereSuperadmin`, `ProblemDetails` on invalid filters, and **no write route at all** (`POST`,
  `PUT`, `DELETE` answer 405).
- **The update path reads the real row before writing it.** `UpdateAsync` used to attach the entity
  that came from the request, which has no original values: that made `BeforeJson` and `AfterJson`
  identical and the audit useless. It now loads the tracked row, copies the client's fields and saves,
  so the audit can answer what changed.
- **CORS allows credentials**, which is what lets the PWA's `HttpOnly` refresh cookie survive a
  cross-origin call.

Verified: `dotnet build` clean, `dotnet test` **70 passed**, and against a live instance an update
whose audit row reads `Label: "forged-meta" → "renamed-by-verify"`, a `SoftDelete` row found by
action, forged audit fields ignored, and `405` on every write attempt against the audit route.

## #15 — The dev server and the API bind the LAN · merged 2026-11-10

`scripts/dev-lan.sh` starts the API on `0.0.0.0:5173` and the Quasar dev server on `0.0.0.0:9000`, so a
phone on the same network opens the app by address instead of only by `localhost`. The flag is
`--hostname`; `--host` is not a Quasar CLI option and aborted the run.

## #14 — Login screen, role-aware navigation and the superadmin dashboard · merged 2026-11-10

The API started refusing anonymous requests, and the PWA had no way to answer: it read the token once
when the module loaded and never showed a login form. This makes the browser client usable again and
gives the superadmin somewhere to work.

- **Session for real**: the token is read on every request, attached, and on a `401` the client tries
  `POST /api/auth/refresh` once (credentials included) before retrying; if that fails the session is
  cleared and the app sends the user to `/login`.
- **Login screen** with the server's message on failure and a return to the interrupted route.
- **Role-aware navigation**: the drawer and the forms follow the role — `lector` has no write
  controls, `operador` edits records and attachments, `admin` opens configuration, the dashboard is
  `superadmin` only. The router guard is the enforcement; the UI just stops offering what would
  bounce back.
- **Superadmin dashboard** with three panels: users (list, create with a role, change roles,
  deactivate), audit (actor/entity/action/date filters, before-after viewer, CSV export) and
  impersonation (pick a user, permanent banner, all writes disabled while impersonating).
- **`/api/users` and `/api/audit` do not exist yet**: the service layer is written against the agreed
  contract and a `404` renders an explicit "API pending" state instead of an error screen.
- **Pending on the API side**: the silent refresh needs the API's CORS policy to call
  `AllowCredentials()`, because the refresh token is an `HttpOnly` cookie that the browser discards on
  a cross-origin call. That one-line change ships with the audit-trail pull request; until it lands,
  refresh only works when the app and the API share an origin.

Verified: `npm test` 86 passed (9 files), `npm run typecheck` clean, `npm run build` succeeded.

Both API gaps named above closed later: `/api/audit` and `AllowCredentials` in #16, `/api/users` in #18.

## #13 — Working rules for the repository · merged 2026-11-10

`AGENTS.md`: how a change lands here — one branch and one concern per pull request, the checks that are
actually run and the command that runs each, the inventory-first order of work, and the security
invariants that are not negotiable.

## #12 — Photos travel as bytes · merged 2026-11-09

Photos travel as bytes, not as base64 inside JSON.

- **`POST /api/attachment/upload` takes multipart form data** (`itemId`, `type`, `fileName`, `file`)
  and streams the file to disk: a third smaller than the base64 payload and no encoded copy in
  memory on the phone. Rejects a missing file, a bad record id and anything over the 20 MB cap.
- **The item payload no longer carries files.** The form saves the record first, then uploads the
  picked files against the id it got back (`afterSave` in `useTreeEditor`), so a new record and its
  first photo work in one pass. The sheet stays busy until the uploads finish; a failed upload is
  reported and does not undo the record.
- **The preview is local until it is saved.** A picked photo is shown through an object URL instead
  of a base64 copy — no encoding, no megabyte string in the form, and the URL is released when the
  row goes away or the sheet closes.
- **Deleting a file prunes its folder** when the last one is gone, so deleting records leaves no
  empty directories behind.

## #7 — Feat/UI polish · merged 2026-11-09 (2 commits, 8 files)

- **The list reads as records, not rows.** No more `0 - Add New` node (creation is a button and an
  empty state), 44px targets, hairline separators, tinted parents, child counts, chevrons on leaves,
  and `4 matches` when searching.
- **The form has a rhythm.** Sections (Details / Attributes / Attachments), the record's own name as
  the title, labels as words (`BuyPrice` → *Buy Price*), one header row for attributes, numbers
  formatted until focused and right-aligned, prose spanning the row.
- **Attachments are a compact list** with a 48px thumbnail instead of full-width cards.
- **Delete moved to an overflow menu** that names what it deletes; the footer is Reset and Save.
- **An empty pane says “Select an item to edit it”.**
- `25d23d9 docs: roadmap…`, `0ca39cc feat(ui): a clearer list, a real form rhythm and fewer traps`

## #6 — Photo opens the camera, a document the file manager · merged 2026-11-09 (1 commit, 2 files)

- A `Photo` attachment opens the camera (`capture="environment"`, “Take a photo” with a gallery
  option); every other type opens the file manager (“Select a file”). New attachments default to
  Photo when the catalogue has it.
- `e5aa36d feat(attachments): a photo opens the camera, a document opens the file manager`

## #5 — Attachments on disk · merged 2026-11-09 (1 commit, 18 files)

- **The original goes to disk, a preview in the row.** `media/items/{itemId}/{guid}{ext}` plus a
  320px JPEG (`ThumbnailBase64`, quality 72), 20 MB cap, SkiaSharp (MIT).
- `GET /api/attachment/{id}/file` streams the original with range support; deleting a row deletes the
  file; `FileBase64` → `ThumbnailBase64` with a migration and a one-time backfill.
- The database went from 16.4 MB to 221 KB once images stopped living in rows.
- `a472273` (branch `feat/attachments-on-disk`)

## #4 — Seed and mobile · merged 2026-11-09 (2 commits, 13 files)

- **The database is not in source.** `imc.db` untracked and ignored, migrations at startup, the
  configuration tree seeded on first run — items are never seeded.
- **Phone-first list and edit sheet**: the list is the page, the form is a full-height sheet with a
  pinned action bar above the safe area.
- `chore(db): stop tracking the SQLite file…`, `feat(ui): phone-first list and edit sheet`

## #3 — Attributes and attachments driven by the constants tree · merged 2026-11-09 (1 commit, 5 files)

- Attribute rows come from the `Attribute` constants: a label with children is a choice list,
  anything else is free text. Attachments follow the `AttachmentType` catalogue.
- `feat(ui): attributes and attachments driven by the constants tree`

## #2 — One tree editor instead of two copies · merged 2026-11-09 (1 commit, 4 files)

- `useTreeEditor` holds load / select / save / delete / dirty state; `TreeMasterDetail` renders the
  split view and the sheet; the pages keep only their own fields (348 → 84 lines for Configuration).
- `feat(ui): one tree editor instead of two copies`

## #1 — UI p0 · merged 2026-11-09 (6 commits, 14 files)

- **Runnable from a clean clone**: `npm ci`, dev server, no missing env var.
- **A feedback layer**: every failure shows the API's own message; every success confirms.
- **Dialog correctness**: validation actually runs, deletes ask for confirmation, buttons cannot
  double-post.
- **Honest state**: loading, empty and error states instead of a blank pane; valid attribute JSON.
- **Reachable from another machine**: API on `0.0.0.0:5173`, development CORS open, the client
  derives the API host from the page.
- **Pages named after their job**: `inventory` and `configuration`, `/` redirects to `/inventory`.

## Auth core: JWT on every route · merged 2026-11-10

The API stopped being an open CRUD endpoint. **C-1** (critical, CWE-306), and with it **A-1**,
**A-2** and **A-3**, are closed: every route now answers `401` without a credential except the five
declared in [endpoints.allow-anonymous.txt](endpoints.allow-anonymous.txt) and the auth endpoints
themselves.

- **JWT authentication wired, and wired in the right order.** `AddIdentityCore` over the existing
  `IdentityDbContext` (the `AspNet*` tables were already in the initial migration, just empty) plus
  `AddJwtBearer` validating issuer, audience, signature and lifetime, with the signing key required
  to be at least 32 bytes at startup. `app.UseAuthentication()` now runs **before**
  `app.UseAuthorization()` — before this, the authorization middleware had no scheme behind it, so no
  `[Authorize]` could ever be satisfied.
- **`POST /api/auth/login`, `/refresh`, `/logout`.** Access token of 15 minutes carrying the roles;
  refresh token of 64 random bytes stored **hashed** (`SHA-256`) with rotation
  (`ReplacedByTokenHash`), revocation on logout, and the cookie is `HttpOnly`, `SameSite=Lax` and
  scoped to `/api/auth` so it never rides along on business requests. Login with a bad password
  answers `401` and counts towards Identity's lockout.
- **Roles and the single superadmin, by construction.** `superadmin`, `admin`, `operador` and `lector`
  seeded at startup; the bootstrap superadmin comes from `Auth:Bootstrap:*` / `AUTH_BOOTSTRAP_*` (no
  default password in source), and the app refuses to boot when more than one account carries the
  role.
- **`[Authorize]` per action, not per controller**, following
  [SECURITY_AUTH_MATRIX.md](SECURITY_AUTH_MATRIX.md): reads need `RequiereLectura`, writes
  `RequiereEscritura`, deletes and the attribute catalogue `RequiereAdmin`, `api/logs/{date}`
  `RequiereAdmin`. The three test endpoints and the read side of `/api/constants` stay anonymous on
  purpose.
- **Production stops leaking traces**: `UseExceptionHandler` outside Development (**M-2**), so the
  developer exception page no longer hands out paths, SQL and versions.
- **The `ParentId` cycle DoS is actually fixed now (C-2).** The first pass only survived two-node
  cycles; the re-audit measured a `Stack overflow` (exit 134) on three or more, because the `Parent`
  walk was seeded with `{ id }` alone. The parent recursion now carries the ancestors it walked plus a
  depth bound, both guards use `IgnoreQueryFilters()` so a cycle through a soft-deleted row is refused
  instead of stored, and `parent` is rendered again (the first pass had silently nulled it).
- **Tests: 57 green**, including a contract test that fails when an anonymous endpoint is not
  declared, login/refresh/revoke coverage, `401` without a token and `403` with the wrong role, the
  single-superadmin invariant, and three new C-2 tests — one of which persists a three-node cycle
  straight into the database, the scenario a controller guard cannot see.
- **The authorization matrix is executed, not described** (`Testing/AuthMatrixTests.cs`): all 17
  protected routes are called with no credential (`401`), with every role below their declared
  minimum (`403`) and with the minimum role itself (authorized), plus token abuse (foreign signing
  key, expired, tampered) and the refresh lifecycle (rotation, replay of the retired token, no
  cookie, authenticated account with no role → `403`). Verified by mutation: widening one policy or
  adding an `[AllowAnonymous]` breaks three tests at once.

## HTTP surface audit: 15 findings · merged 2026-11-10

The HTTP surface audited end to end and reproduced in a live run: **15 findings — 2 critical, 3 high,
6 medium, 4 low**. Full report in [docs/security/2026-11-10-audit.md](docs/security/2026-11-10-audit.md);
the per-route decision is [SECURITY_AUTH_MATRIX.md](SECURITY_AUTH_MATRIX.md), and the five routes that
may stay anonymous are declared in
[endpoints.allow-anonymous.txt](endpoints.allow-anonymous.txt). Neither critical is fixed yet: C-2 is
in progress, C-1 is phase (b) of the roadmap.

- **C-1 (critical, CWE-306) — the API authenticates nobody.** `Program.cs:117-124` calls
  `UseAuthorization()` with no authentication scheme registered and never calls `UseAuthentication()`,
  and every `[Authorize]` is commented out (`Controllers.cs:15,138,259`, `BaseController.cs:26`).
  Confirmed without a credential: `POST`, `PUT`, `DELETE`, `upload` and `/file` all answered 200/204
  — full inventory read, write, delete and arbitrary file upload from anyone who reaches the port.
- **C-2 (critical, CWE-674) — a `ParentId` cycle kills the process.** Two anonymous `PUT`s (a
  record's parent set to its own child) leave EF's `Children` navigation closed on itself and the DTO
  constructors recurse until the stack is gone (`DTOs.cs:49-51` and `:132-134`, plus the twin in
  `Controllers.cs:63-70`). Confirmed: `GET /api/item` and `GET /api/constants` stop answering and the
  process dies (`exit 134`, the overflow repeated 23,792 times); the database stays poisoned, so it
  dies again on the first `GET` after a restart.
- **A-1 (high, CWE-862) — destructive writes and deletes with no credential, and the delete reaches
  the disk.** `ItemController.Delete` (`Controllers.cs:76-95`) removes the attachment files before the
  rows, and `AttachmentStorage.Delete` is not a move to a bin: a loop over `1..N` empties the
  inventory and its photos for good.
- **A-2 (high, CWE-915) — mass assignment of `Id`, `IsActive` and `UpdatedBy`.** The client sets the
  audit field itself (`Models.cs:7-15`, `BaseRepository.cs:135-140`); `POST` returned
  `"updatedBy":"attacker"` verbatim, so every change can be filed under a name the attacker chooses.
  An explicit `Id` also collides with a live primary key and answers 500 instead of 409.
- **A-3 (high, CWE-532 / CWE-200) — `GET /api/logs/{date}` serves raw logs.** `LogsController.cs:19-37`
  returns the day's file as JSON, absolute server paths and full exception `ToString()`s included: a
  map of the host and of the code paths that fail.

## Merged — `feat/tests` (stacked on `feat/pwa`, 2026-11-09)

The suite stops lying, and grows teeth.

- **The two failing tests were a real bug.** `BaseRepository.GetById` used `AsNoTracking`, so EF's
  relationship fix-up never filled a record's children — the list endpoint got them only because it
  loads the whole table. `GetById` now tracks and loads the entity's collections; all six cases of
  `GetById_ReturnsChildEntities` pass, and `GET /api/item/{id}` returns a record with its children.
- **API integration tests** (`Testing/ApiTests.cs`, `WebApplicationFactory`): a throwaway SQLite
  database and media folder per run, migrations and seeding included. Nine tests over the real HTTP
  surface — tree and panels, create/read/update/delete, children on the detail endpoint, attachment
  upload → served bytes compared by SHA-256 → delete removes row and file, `attachmentCount` on the
  tree, the seeded catalogue's data types, and a record without a name being refused (which needed
  `[Required]` on `Item.Label`).
- **UI unit tests** (Vitest + jsdom, `npm test`): the tree editor's state machine (dirty tracking,
  child and duplicate forms, parent options, validation, confirmed delete) and the attribute editor's
  typed rows (money keypad, prose textarea, date picker, text kept verbatim, custom keys, filter).
- `AttachmentStorage` takes an optional `Media:Root` so tests (and a deployment) can keep files out
  of the project folder.
- **Deleting a record deletes its files.** The API soft-deleted the record and left the attachment
  rows and their files behind, although the confirm dialog says “Its attachments are deleted with
  it.” `ItemController.Delete` now removes the files and the rows first; a test proves the file is
  gone from disk and the list endpoint returns `204`.
- **A review pass over the suite**: the search logic moved out of the component into
  `services/search.ts` so it can be tested at all (14 cases: key/value matching, case and punctuation
  insensitivity, malformed JSON, parents kept for context, the count), and the API gained 8 cases it
  was missing — 404 on an unknown id, `400` on a mismatched PUT, range requests on the file endpoint,
  attachments by item (`204` when empty), the group endpoint, a label over the column length, an
  invalid upload, and the delete cascade. UI cases added for the attachment editor (camera vs file
  manager by type, preview vs icon, remove/add, the saved-file link) and for the tree editor's save
  paths (update vs create, the sheet closing only on success, a failed load keeping the page usable,
  a failed delete keeping the record, and the page's own snapshot counting as dirty).

## Merged — `feat/pwa` (stacked on `feat/search-values`, 2026-11-09)

Installable on a phone's home screen.

- **PWA mode on** (`quasar mode add pwa`): manifest, service worker and the Workbox runtime generated
  by the build; `src-pwa/register-sw.ts` replaces the scaffold comment block and reports an upgrade
  through a window event, which `src/boot/pwa.ts` turns into a notification with a Reload action
  instead of reloading under someone's edits.
- **Its own icon set**: 128/192/256/384/512 plus maskable 192/512, Apple touch icons and a pinned-tab
  mark, all generated from one gradient-and-monogram design on `#0f4c81`; the manifest names the app
  `IMC — Inventory Management`, `short_name` `IMC`, `display: standalone`, `start_url: /`.
- Verified on the built bundle (`quasar build -m pwa`, served from `dist/pwa`): manifest linked and
  parsed (7 icons), service worker registered at scope `/`, the app renders the six records, and with
  the network off the shell still loads and reports `Cannot reach the API`.

## Merged — `feat/search-values` (2026-11-09)

Search now looks inside the records, not only at their names.

- **The list no longer disappears while searching.** The match counter had taken the tree's `v-else`
  branch, so typing a query replaced the rows with a line of text — a regression from the polish
  round. The counter and the tree now share one branch and the tree keeps rendering.
- **Search matches attribute values.** `SN-0042` finds the record that carries it, `serial` finds the
  records that have that key at all, and a matched row shows the pair under its name
  (`Serial · SN-0042`) with the count reporting only rows that matched themselves, not the parents
  kept for context.

## Merged — `feat/ui-polish` (PR #8, 9 commits)

Guarded form, typed values, attributes of your own, duplicate, a filter and the committed inventory.

- **Unsaved work is protected.** Closing the sheet or picking another record on a desktop asks
  before dropping edits; the form says `Unsaved changes` while it differs from what was loaded.
  `bfd7400 feat(ui): ask before throwing edits away`
- **The list marks records that carry files.** One grouped query sets `AttachmentCount`; a paperclip
  appears on those rows. `17b4231 feat(api): tell the list which records carry files`
- **Attribute definitions have a data type.** `Constants.DataType` (Text, Number, Money, Date,
  LongText) with a migration and a one-time backfill of the seeded attributes; a “Data type” select
  in Configuration. `575d29b feat(attributes): an attribute definition carries a data type`
- **The item form follows the type.** Decimal keypad and separators for money, the device calendar
  for dates, a textarea for prose, and text that is no longer scrubbed to digits (`SN-0042` used to
  save as `-0042`). `93b4f50 feat(ui): typed values, attributes of your own, and a filter`
- **Attributes only one item needs.** “Add custom” adds a key of your own, stored on that item and
  rebuilt on load; a value without a name, or two rows with the same name, is refused.
- **Duplicate a record** from the overflow menu: same parent, icon and attributes, named
  `<name> (copy)`; files are not copied.
- **Filter inside the form** once there are more than five attributes.
- **Add a child from the open record**, so the parent does not have to be found in a list again.
  `90638db feat(ui): add a child from the record you are looking at`
- **The inventory is committed** and refreshed by `.githooks/pre-commit`, with `--no-stamp` keeping
  it byte-identical when nothing changed. `2c03125 chore: commit the inventory and keep it in step with the code`
- **Roadmap rewritten** to what is actually left. `89995d1 docs: the roadmap records what landed`,
  `a305759 fix(ui): put the attribute actions above the rows`

