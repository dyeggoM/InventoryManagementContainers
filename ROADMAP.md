# Roadmap

What is deliberately not built yet, in the order it is worth doing.

## Done

- **Per-item custom attributes.** A row can be added with a key the catalogue does not define; it is
  stored in that item's `Attributes` JSON and survives a reload. No global side effect: the label
  belongs to the item that has it, and a later decision to promote it to Configuration is a manual
  one, which is the answer to the open question this item used to carry.
- **Typed attribute values.** `Constants.DataType` (`Text`, `Number`, `Money`, `Date`, `LongText`)
  drives the control, the keypad, the alignment and the formatting. Set in Configuration, per
  attribute; null means Text so old rows keep working.
- **Duplicate a record.** Kits repeat: the overflow menu copies parent, icon and attributes into a
  new record named `<name> (copy)`. Files are not copied.
- **Installable on the home screen.** PWA mode, manifest, icons and a service worker that serves the
  shell offline.
- **Tests with teeth.** `GetById` was returning no children at all (the two failures on `main` were
  a real bug, not flaky tests); nine API integration tests now drive the app over HTTP against a
  throwaway database, and Vitest covers the tree editor's state machine and the attribute editor.
- **Uploads without base64.** A multipart endpoint takes the file as bytes; the form saves the
  record first and uploads against the id it gets back.
- **Search by attribute value.** `SN-0042` finds the record that carries it and shows the pair under
  the name; the count only counts rows that matched themselves.

## Autenticación y auditoría

La ronda de seguridad del 2026-11-10 convirtió "la API no autentica" en una medición: 15 hallazgos
—2 críticos, 3 altos, 6 medios, 4 bajos—, con los dos críticos reproducidos en ejecución. El informe
completo es [docs/security/2026-11-10-audit.md](docs/security/2026-11-10-audit.md); la decisión de
qué debe exigir cada ruta es [SECURITY_AUTH_MATRIX.md](SECURITY_AUTH_MATRIX.md).

- [x] **(a) Matriz de autenticación y autorización** — `done` · dueño: unfiltered.
  `SECURITY_AUTH_MATRIX.md` clasifica las 23 rutas reales: **18 requieren auth, 5 pueden quedar
  anónimas** (los tres `/test` y la lectura del catálogo `constants`). Las 5 están declaradas, una
  por línea, en `endpoints.allow-anonymous.txt`, que es la fuente del test de contrato: una ruta
  anónima que no esté listada hace fallar la suite. Roles decididos: `superadmin` > `admin` >
  `operador` > `lector`, aplicados **por acción y no por controlador**, porque los `override` de los
  hijos reutilizan la ruta y un `[Authorize]` de clase se pierde con facilidad. Verificada en
  ejecución contra `127.0.0.1:5099`: mass assignment de `UpdatedBy` y DoS por ciclo de `ParentId`
  reproducidos, no teóricos.

- [ ] **(b) Identity + JWT y `[Authorize]` por defecto** — `todo` · dueño: openai-coder.
  Cierra **C-1** (CWE-306) y, con él, **A-1**, **A-2** y **A-3**. Registra la autenticación
  (`AddAuthentication` + `AddJwtBearer`) y **`app.UseAuthentication()` antes de
  `app.UseAuthorization()`** en `IMCAPI/Program.cs:117-124` — hoy sólo existe el segundo, sin ningún
  esquema, así que ningún `[Authorize]` podría satisfacerse. Añade `POST /api/auth/login` emitiendo
  el token que `imcui/src/services/api.ts:8` ya lee de `localStorage`, siembra los cuatro roles en
  `DB/DatabaseSeeder.cs:17` (las tablas `AspNetUsers`/`AspNetRoles` existen desde la migración
  inicial y están vacías), y activa `[Authorize]` por acción con `[AllowAnonymous]` sólo en las 5
  rutas de `endpoints.allow-anonymous.txt`. El test de contrato del punto (a) es la red que impide
  que la fase (b) deje una ruta abierta por descuido.
  Fuera de alcance en esta fase: **B-4** (el cliente lee el token una sola vez, al importar el
  módulo, `api.ts:8`); va con la UI de (c).
  Decisiones de Diego (2026-11-10): **JWT** (access corto + refresh en cookie `HttpOnly` guardado
  hasheado en servidor), **MFA para `superadmin` en la segunda iteración** (queda para después de
  (c)), y **`Auth:Enforce` activo** desde el primer despliegue de esta fase.

- [ ] **(c) Dashboard superadmin: suplantación y CRUD de usuarios** — `todo` · dueños:
  openai-coder (API) + opencode-coder (UI) · depende de (b).
  Alta, baja, reset de contraseña y cambio de rol desde la propia app, más suplantación explícita
  ("entrar como") auditada: el token de suplantación debe llevar quién suplanta a quién, caducar
  corto y dejar rastro en el historial de (d). Sin (b) no hay `ClaimsPrincipal` del que colgar
  ninguna de las dos cosas.

- [ ] **(d) Auditoría de cambios con interceptor EF** — `todo` · dueño: openai-coder · depende de (b).
  Un `SaveChangesInterceptor` que registre entidad, `Id`, operación (`Added`/`Modified`/`Deleted`),
  **usuario real tomado de `User.Identity`** —nunca del cuerpo: eso es exactamente **A-2**,
  CWE-915— y marca de tiempo. Es la contrapartida de servidor al campo `UpdatedBy` de
  `DB/Entities/Models.cs:7` y la base del historial que la suplantación de (c) necesita para ser
  auditable.

### Prioridad 1 de corrección: C-2 — DoS por ciclo de `ParentId` (CWE-674)

- [x] **C-2** — `done` (2026-11-10) · dueño: openai-coder · **prioridad 1 de corrección**.
  Resuelto con conjunto de visitados en los dos constructores de DTO y en `ApplyAttachmentCount`,
  más validación de ciclo en `ItemController.Put` y un `Put` nuevo en `ConstantsController` (que
  devuelve `400`). Verificado: `dotnet build IMCAPI/IMCAPI.csproj` sin errores, `dotnet test
  Testing/Testing.csproj` **36/36 en verde** (incluye `PutItem_CannotSetAChildAsItsParent_AndTreeRemainsReadable`),
  y reproducción en sandbox propio: `PUT` con `ParentId` de su propio hijo → `400`, `GET /api/item`
  → `200` acto seguido, y un ciclo inyectado directamente en SQLite (base ya envenenada) → `200`,
  el proceso sigue vivo. Historia original para contexto:
  Es el único hallazgo que tumba el proceso (`exit 134`, SIGABRT) y hoy se dispara con dos `PUT`
  anónimos: se pone el `ParentId` de un registro al de su propio hijo, EF hace *fix-up* de las dos
  direcciones y la navegación `Children` queda cerrada sobre sí misma. `GET /api/item` o
  `GET /api/constants` no responden y matan el servicio; la base queda envenenada, así que vuelve a
  morir en el primer `GET` tras reiniciar.
  Corte estructural de la recursión en los dos constructores de `IMCAPI/DB/Entities/DTOs.cs`
  (`ItemDTO` :39-54 y `ConstantsDTO` :119-136) y en `ApplyAttachmentCount`
  (`IMCAPI/Controllers/Controllers.cs:63-70`) con conjunto de visitados o profundidad máxima; y
  validación de ciclo en `ItemController.Put` (`Controllers.cs:43-61`) y en un `Put` nuevo de
  `ConstantsController` —hoy no tiene `override` y cae en `BaseController.cs:105`— devolviendo
  `BadRequest`. Es la causa raíz, no el síntoma: cortar sólo la recursión deja el ciclo en la base.
  Aceptación: test de regresión (padre con `ParentId` de su propio hijo → `400`, y `GET /api/item`
  después → `200` con el árbol intacto), `dotnet build IMCAPI/IMCAPI.csproj` sin errores y
  `dotnet test Testing/Testing.csproj` en verde.

### Resto de hallazgos, por orden de corrección

Cerrado C-2 y en marcha (b), el orden que fija el informe (§5) es: **A-1** y **A-2** (roles en
`Delete`/`Post`/`Put`/`upload` y `UpdatedBy` desde `User.Identity`; mecánicos en cuanto exista
identidad), **A-3 + M-1** (`[Authorize(Roles = "superadmin,admin")]` en `LogsController` y `date`
validada por regex, para que la protección no dependa de que el servidor no decodifique `%2F`),
**M-4/M-5/M-6** (`BaseRepository`: `Add` con `Id = 0`, `UpdateAsync`/`Delete` que devuelvan `bool` y
traducción a `404`/`409`, `IgnoreQueryFilters()` para que el borrado físico sea alcanzable),
**M-2/M-3** (`UseExceptionHandler` fuera de Development y validación del `itemId` antes de escribir
en disco) y los cuatro bajos (**B-1..B-4**) en un solo pase.

## Entrada masiva desde plantilla

Entering a kit still means one record at a time. Duplicate covers the repeated-attributes case; the
next step is a CSV/Excel template the user fills in (parent, children, attributes per row, one column
per attribute key) and uploads, with a preview of what will be created before anything is written.

Waits on (b): an import endpoint is the last thing to leave open, and it has to respect the cycle
validation C-2 introduces — an imported row whose parent points at its own child is the same DoS
entering by a different door.
