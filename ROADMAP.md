# Roadmap

What is deliberately not built yet, in the order it is worth doing.

### Deuda abierta del archivado de registros (`feat/api-items-archive`)

- **Los hijos de un registro borrado quedan como raíces** (`ParentId = null`), decisión implícita: si se
  prefiere borrar en cascada o bloquear el borrado con hijos, hay que elegirlo explícitamente.
- **Las filas de `ItemsArchive` no caducan**: no hay retención ni purga, y `AttachmentsJson` puede
  crecer con los adjuntos. Definir política (por antigüedad o por tamaño) antes de que haya volumen.
- **Restaurar no devuelve los adjuntos**: los ficheros se borran del disco en el `DELETE`, así que un
  registro restaurado vuelve sin ellos. Si se quiere recuperación completa, hay que pasar a papelera
  de ficheros en vez de borrado.
- **`GET /api/item/archive` hace `COUNT(*)` por página** y no tiene índice por `Label`; revisar cuando
  la tabla crezca (misma deuda que `AuditLogs`).

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

## Entrada masiva desde plantilla

Entering a kit still means one record at a time. Duplicate covers the repeated-attributes case; the
next step is a CSV/Excel template the user fills in (parent, children, attributes per row, one column
per attribute key) and uploads, with a preview of what will be created before anything is written.

Waits on (b): an import endpoint is the last thing to leave open, and it has to respect the cycle
validation C-2 introduces — an imported row whose parent points at its own child is the same DoS
entering by a different door.
