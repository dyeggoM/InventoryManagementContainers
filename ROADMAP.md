# Roadmap

What is deliberately not built yet, in the order it is worth doing.

### Deuda abierta del archivado de registros (`feat/api-items-archive`)

- [x] **Los hijos de un registro borrado quedan como raíces** — resuelto: ahora el borrado devuelve
  `409 Conflict` mientras existan hijos; primero hay que moverlos o borrarlos, conservando intactas
  las relaciones existentes.
- [x] **Las filas de `ItemsArchive` no caducan** — `done` (2026-11-11). `POST /api/item/archive/purge`
  (superadmin) borra las filas más antiguas que la retención, configurable con
  `Archive:Retention:Days` / `ARCHIVE_RETENTION_DAYS` y **desactivada por defecto a propósito**: nada se
  borra salvo que alguien la configure o pase `olderThanDays`. Deja fila `ArchivePurge` en la auditoría.
  Sigue en pie la decisión de plazo: el mecanismo está, el número es tuyo.
- **Las filas de `ItemsArchive` no caducan (tamaño, no antigüedad)**: no hay límite por número de filas
  ni por tamaño de `AttachmentsJson`. Si el volumen crece más rápido que el calendario, la purga por
  fecha no basta.
- **Restaurar no devuelve los adjuntos**: los ficheros se borran del disco en el `DELETE`, así que un
  registro restaurado vuelve sin ellos. Si se quiere recuperación completa, hay que pasar a papelera
  de ficheros en vez de borrado.
- [x] **`GET /api/item/archive` hacía `COUNT(*)` por página** — `done` (2026-11-11). El total se
  responde una vez, en la página 1; las siguientes devuelven `total: null` y un `hasMore` calculado
  pidiendo una fila de más. Igual en `GET /api/audit`. El índice por `Label` (migración 2026-11-11)
  sigue ahí, así que el filtro tampoco es un scan. Queda la deuda de las páginas profundas: `OFFSET`
  sigue recorriendo lo saltado; se resolverá con paginación por cursor si llega el volumen.

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

- [x] **(b) Identity + JWT y `[Authorize]` por defecto** — `done` (2026-11-10) · dueño: openai-coder.
  Cierra **C-1** (CWE-306) y, con él, **A-1**, **A-2** y **A-3**: la API ya no responde datos de
  negocio sin credencial. Entregado: `AddIdentityCore` + `AddJwtBearer` con validación de emisor,
  audiencia, firma y vida (clave de ≥32 bytes exigida al arrancar), `app.UseAuthentication()` **antes**
  de `UseAuthorization()` (`Program.cs`), políticas `RequiereLectura/Escritura/Admin/Superadmin`,
  `POST /api/auth/login|refresh|logout` (`AuthController.cs`; refresh de 64 bytes aleatorios guardado
  **hasheado** con SHA-256, rotación con `ReplacedByTokenHash`, revocación y cookie `HttpOnly`
  `SameSite=Lax` limitada a `/api/auth`), entidad `RefreshToken` + migración
  `20261110062651_AddRefreshTokens`, roles sembrados y **bootstrap del único superadmin** desde
  `Auth:Bootstrap:*`/`AUTH_BOOTSTRAP_*` con invariante que impide arrancar con más de uno,
  `UseExceptionHandler` fuera de Development, y `[Authorize]`/`[AllowAnonymous]` por acción según la
  matriz (los tres endpoints de auth añadidos a `endpoints.allow-anonymous.txt`).
  Verificación: `dotnet build` sin errores (queda un warning de nulabilidad preexistente en
  `FileLoggerProvider.cs`), `dotnet test` **48/48**, y sonda en vivo sobre `127.0.0.1:5112`:
  `GET /api/item`, `/api/attachment`, `/api/logs/*` y `POST /api/item` → **401** sin token;
  `/api/item/test` y `/api/constants` → **200** sin token; login correcto → **200** con
  `access_token` (15 min) y cookie `imc_refresh` `HttpOnly`; password mala → **401**; con token
  válido → **200** en item, POST y logs (el superadmin de bootstrap).
  Deuda declarada para la siguiente ronda: rate limiting en `/api/auth/*`, MFA de superadmin (segunda
  iteración), **B-4** (el cliente lee el token una sola vez, `api.ts:8`) y que `Auth:Enforce=false`
  desactiva las comprobaciones de rol en bloque — es herramienta de diagnóstico local, no un modo
  para producción.

- [x] **(c) Dashboard superadmin: suplantación y CRUD de usuarios** — `done` (2026-11-11).
  Entregado en #18/#14 y cerrado en esta ronda: alta, baja, cambio de rol y **contraseña** desde la
  app (`POST /api/users` devuelve `initialPassword` de un solo uso; `POST /api/users/{id}/password`
  revoca los refresh tokens y, vía `sstamp`, los access tokens ya emitidos), y suplantación **por rol
  y jerarquía** como pedía la restricción de abajo: `superadmin → admin|operador|lector`,
  `admin → operador|lector`, `operador → lector`, nunca `superadmin` como destino, token de 7 minutos
  que conserva la identidad real en `sub` y añade `act`/`impersonated_role`, escrituras bloqueadas por
  middleware y el par `ImpersonateStart`/`ImpersonateEnd` en la auditoría. La suplantación **no se
  renueva**: al caducar, la sesión vuelve al usuario real.

  **Restricciones anotadas por Diego (2026-11-11)**: la suplantación es **por rol, nunca por usuario
  concreto** — superadmin puede "entrar como" admin u operador para validar funcionamientos, pero
  nunca como un admin o un user específico. El alcance es siempre hacia abajo en la jerarquía:
  `superadmin → admin → operador → lector`, `admin → operador → lector`, `operador → lector`;
  `lector` no suplanta. Lo que hay hoy (`POST /api/user/{id}/impersonate`,
  `UserController.Impersonate`) recibe un `id` de usuario concreto y solo exige ser superadmin, de
  modo que el contrato tiene que pasar a rol + jerarquía antes de cerrar (c).

- [x] **(d) Auditoría de cambios con interceptor EF** — `done` (2026-11-10, PR #16) · dueño: openai-coder.
  Entregado: `AuditSaveChangesInterceptor` como único escritor, `AuditLog` con antes/después, actor
  tomado del token, redacción de secretos y `GET /api/audit` sólo para superadmin. Lo que sigue son
  dos mejoras derivadas de las notas de Diego, no trabajo pendiente de (d).
  Lo que se pidió: un `SaveChangesInterceptor` que registrara entidad, `Id`, operación (`Added`/`Modified`/`Deleted`),
  **usuario real tomado de `User.Identity`** —nunca del cuerpo: eso es exactamente **A-2**,
  CWE-915— y marca de tiempo. Es la contrapartida de servidor al campo `UpdatedBy` de
  `DB/Entities/Models.cs:7` y la base del historial que la suplantación de (c) necesita para ser
  auditable.

- [x] **(d.1) Anchos de `AuditLog`** — `done` (2026-11-11). `Action` 32 → 64 y `EntityId` 512 → 1024,
  con migración `20261111142916_WidenAuditLogAndIndexArchiveLabel` (en SQLite es solo metadato porque
  ambas columnas son `TEXT`; el modelo es lo que leería una migración a SQL Server). `BeforeJson` y
  `AfterJson` siguen sin tope a propósito: son texto libre y el límite real lo pone el tamaño de la
  fila. Además `ItemsArchive.Label` tiene índice, que era la deuda del listado del archivo.
- [ ] **(d.2) `ItemsArchive` como ledger de borrado** — `todo` · dueño: pendiente. Su intención
  original para los borrados era una **tabla independiente sin relaciones** con el registro borrado
  más quién y cuándo: eso ya existe como `ItemsArchive` (`DB/Entities/ItemArchive.cs`,
  deliberadamente sin FK, con `DeletedAtUtc`/`DeletedById`/`DeletedBy` y `RestoredAtUtc`), así que lo
  correcto es conservar `AuditLogs` como historial de cambios y dejar `ItemsArchive` como ledger de
  borrado, sin duplicar la misma información en los dos sitios.

- [x] **(e) `RefreshToken`: fechas solo del servidor** — `done` (2026-11-11).
  `Refresh` no tiene parámetro de cuerpo, así que el cliente no puede influir en las fechas; el test
  `Refresh_TakesItsDatesFromTheServer_NotFromTheBody` fija el contrato: un cuerpo con fechas a diez
  años se ignora y el token de acceso sigue saliendo a 15 minutos y la fila de refresh a 7 días.
  Queda solo la decisión cosmética de parametrizar los 7 días de `AuthController:71`.
- [x] **(f) Rate limiting en `/api/auth/*`** — `done` (2026-11-11). Ventana fija por dirección de
  cliente (`Auth:RateLimit:PermitLimit` 10 / `Auth:RateLimit:WindowSeconds` 60 por defecto) que
  responde `429`; cubre solo `AuthController`, y las rutas de negocio quedan fuera. La suite sube el
  límite para no ser intermitente y `RateLimitTests` lo baja a 3 con una fábrica propia para recorrer
  la ventana.
- [x] **(g.1) `ImpersonateEnd` cuando se sale desde la app** — `done` (2026-11-11). El botón *Salir*
  llama a `POST /api/auth/stop-impersonation` con el token prestado y adopta el que devuelve, así que
  el par `ImpersonateStart`/`ImpersonateEnd` queda completo. Si la llamada falla (red, token ya
  caducado, `403`) vuelve al token propio guardado y, sin él, cierra la sesión: nunca deja el aviso
  arriba sobre un token que ya no suplanta. Sin llamada no hay fila de fin, y eso es lo honesto.
- [ ] **(g) Suplantación: dejar rastro en la fila de auditoría** — `low` · dueño: pendiente. Con la
  suplantación por rol ya no hay usuario suplantado, así que `AuditLogs.ImpersonatedUserId` queda
  siempre nulo; las filas `ImpersonateStart`/`ImpersonateEnd` llevan el rol en `EntityId`, pero una
  escritura hecha mientras se suplanta (hoy imposible por el middleware) no podría distinguirse.

### Prioridad 1 de corrección: C-2 — DoS por ciclo de `ParentId` (CWE-674)

- [x] **C-2** — `done` (2026-11-10, tras re-auditoría) · dueño: openai-coder + revisión de Unfiltered.
  Resuelto en dos tiempos: el corte con conjunto de visitados en `Children` y en
  `ApplyAttachmentCount`, la validación de ciclo en `ItemController.Put` y `ConstantsController.Put`,
  y — detectado por la re-auditoría de Unfiltered sobre el primer intento — el arreglo del salto
  `Parent`, que sólo propagaba `{ data.Id }` y dejaba caer el proceso con ciclos de **3 o más** nodos
  (medido: `Stack overflow`, exit 134). Ahora el padre se construye con los ancestros ya recorridos y
  un tope de profundidad, y los dos guardas usan `IgnoreQueryFilters()` para ver también las filas
  inactivas (antes un ciclo a través de un registro borrado lógicamente se aceptaba con `204`).
  Verificación: `dotnet test` **48/48** con tres tests nuevos — uno **persiste** un ciclo de 3
  escribiendo directo en la BD (import, `UPDATE` de mantenimiento o base escrita antes del guarda), el
  registro inactivo como eslabón del ciclo, y el contrato de que `parent` vuelve a venir poblado; más
  sonda en vivo: ciclo de 3 en la BD → `GET /api/item` **200** en 0.01 s con el proceso vivo.
  Deuda conocida: `ParentId` inexistente o `0` sigue dando **500** por FK (debería ser `400`), y la
  validación está duplicada en los dos controladores (~55 líneas) — candidata a helper compartido.
  Antes de desplegar sobre una base existente, correr:
  `SELECT i.Id FROM Items i JOIN Items p ON p.Id = i.ParentId WHERE i.Id = p.ParentId AND i.Id <> i.ParentId`.
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

## Dashboard en móvil

- [ ] **(h) Los paneles del dashboard en pantalla pequeña** — `low` · dueño: opencode-coder. Las
  tablas de usuarios y auditoría y el panel de archivo se diseñaron en escritorio: en un móvil las
  columnas no caben y hay que desplazar en horizontal. Convertirlos en tarjetas por fila (o en una
  tabla con columnas prioritarias y un detalle desplegable), sin tocar la lógica ni los servicios.
  Prioridad baja: primero lo que bloquea el trabajo diario.

## Entrada masiva desde plantilla

- [ ] **(i) Importación desde Excel** — `in progress` · dueño: openai-coder + code-orchestrator.
  Hecho: `POST /api/import/preview` (`Controllers/ImportController.cs`, admin) lee un `.xlsx`/`.xls`
  con `ExcelDataReader` (`Services/ItemImporter.cs`) y devuelve el árbol que el fichero describe con
  cada fila en verde, amarillo o rojo y el motivo. **No escribe nada.** Medido sobre el fichero real
  de 578 filas: 361 verdes, 94 amarillas, 105 rojas, 455 importables.
  Hecho también: `POST /api/import` (una transacción, rojas excluidas, idempotente por código) y la
  pantalla *Importar desde Excel* con el árbol coloreado, filtros y desmarcado de amarillos.
  Decidido con Diego (2026-11-13): una fila = un elemento físico (los repetidos son unidades, no
  duplicados), y `Attributes.Code` es opcional pero **único** cuando viene.
  Cerrado también: el patrón CSV (coma, punto y coma o tabulador) y el acceso — `/dashboard` pasa a
  `ADMIN_ROLES` (`router/guards.ts`) y cada panel se oculta según la política de su endpoint, así que
  un `admin` ve *Importar* y *Suplantación* y no ve usuarios, auditoría ni archivo.
  Primera importación real sobre la base de desarrollo: 455 registros, 90 grupos, 5 raíces; la segunda
  pasada del mismo fichero creó 0.

Entering a kit still means one record at a time. Duplicate covers the repeated-attributes case; the
next step is a CSV/Excel template the user fills in (parent, children, attributes per row, one column
per attribute key) and uploads, with a preview of what will be created before anything is written.

Waits on (b): an import endpoint is the last thing to leave open, and it has to respect the cycle
validation C-2 introduces — an imported row whose parent points at its own child is the same DoS
entering by a different door.
