# Matriz de autenticación y autorización — IMCAPI

Estado actual: **ningún endpoint exige autenticación**. Este documento es la decisión de qué debe
exigir cada ruta y con qué rol mínimo; el análisis completo está en
[`docs/security/2026-11-10-audit.md`](docs/security/2026-11-10-audit.md).

Convención: `REQUIERE_AUTH (rol mínimo)` | `PUEDE_SER_ANONIMO` | `A_REVISAR`.
Roles: `superadmin` > `admin` > `operador` > `lector`.

## Matriz

| # | Verbo y ruta | Implementación | Hoy | Decisión |
|---|---|---|---|---|
| 1 | `GET /api/item/test` | `Controllers/BaseController.cs:22` | anónimo | `PUEDE_SER_ANONIMO` — devuelve `new T()`, health check sin datos |
| 2 | `GET /api/item` | `Controllers/Controllers.cs:97` | anónimo | `REQUIERE_AUTH (lector)` |
| 3 | `GET /api/item/{id:int}` | `Controllers/BaseController.cs:75` | anónimo | `REQUIERE_AUTH (lector)` |
| 4 | `PUT /api/item/{id:int}` | `Controllers/Controllers.cs:43` | anónimo | `REQUIERE_AUTH (operador)` |
| 5 | `POST /api/item` | `Controllers/Controllers.cs:27` | anónimo | `REQUIERE_AUTH (operador)` |
| 6 | `DELETE /api/item/{id:int}` | `Controllers/Controllers.cs:76` | anónimo | `REQUIERE_AUTH (admin)` |
| 7 | `GET /api/attachment/test` | `Controllers/BaseController.cs:22` | anónimo | `PUEDE_SER_ANONIMO` |
| 8 | `GET /api/attachment` | `Controllers/BaseController.cs:49` | anónimo | `REQUIERE_AUTH (lector)` |
| 9 | `GET /api/attachment/{id:int}` | `Controllers/BaseController.cs:75` | anónimo | `REQUIERE_AUTH (lector)` |
| 10 | `PUT /api/attachment/{id:int}` | `Controllers/BaseController.cs:105` | anónimo | `REQUIERE_AUTH (operador)` |
| 11 | `POST /api/attachment` | `Controllers/Controllers.cs:150` | anónimo | `REQUIERE_AUTH (operador)` |
| 12 | `POST /api/attachment/upload` | `Controllers/Controllers.cs:163` | anónimo | `REQUIERE_AUTH (operador)` |
| 13 | `DELETE /api/attachment/{id:int}` | `Controllers/Controllers.cs:195` | anónimo | `REQUIERE_AUTH (operador)` |
| 14 | `GET /api/attachment/{id:int}/file` | `Controllers/Controllers.cs:209` | anónimo | `REQUIERE_AUTH (lector)` |
| 15 | `GET /api/attachment/item/{id:int}` | `Controllers/Controllers.cs:231` | anónimo | `REQUIERE_AUTH (lector)` |
| 16 | `GET /api/constants/test` | `Controllers/BaseController.cs:22` | anónimo | `PUEDE_SER_ANONIMO` |
| 17 | `GET /api/constants` | `Controllers/Controllers.cs:269` | anónimo | `PUEDE_SER_ANONIMO` — catálogo de atributos, sin datos de inventario |
| 18 | `GET /api/constants/{id:int}` | `Controllers/BaseController.cs:75` | anónimo | `PUEDE_SER_ANONIMO` |
| 19 | `GET /api/constants/group/{group}` | `Controllers/Controllers.cs:291` | anónimo | `PUEDE_SER_ANONIMO` |
| 20 | `PUT /api/constants/{id:int}` | `Controllers/BaseController.cs:105` | anónimo | `REQUIERE_AUTH (admin)` |
| 21 | `POST /api/constants` | `Controllers/BaseController.cs:143` | anónimo | `REQUIERE_AUTH (admin)` |
| 22 | `DELETE /api/constants/{id:int}` | `Controllers/BaseController.cs:169` | anónimo | `REQUIERE_AUTH (admin)` |
| 23 | `GET /api/logs/{date}` | `Controllers/LogsController.cs:19` | anónimo | `REQUIERE_AUTH (admin)` + `date` validada |

Resumen: **18 requieren autenticación, 5 pueden quedar anónimas** (3 `test` + catálogo `constants`
de lectura), `logs` pasa de `A_REVISAR` a `REQUIERE_AUTH (admin)` porque sirve rutas internas y
trazas.

Las 5 rutas anónimas están declaradas, una por línea, en
[`endpoints.allow-anonymous.txt`](endpoints.allow-anonymous.txt). Ese archivo es la fuente del test
de contrato: si aparece una ruta anónima que no esté listada, el test falla.

## Verificación independiente del auditor (reproducida por Hermes, 2026-11-10)

Sandbox con copia del repo, SQLite temporal, `dotnet run` en `127.0.0.1:5099`:

- `GET /api/item/test` → `200` sin ninguna cabecera de autenticación.
- `POST /api/item {"label":"verify-anon"}` → `200` y crea el registro.
- `POST /api/item {"updatedBy":"attacker"}` → `200`; la respuesta devuelve `"updatedBy":"attacker"`
  → **mass assignment confirmado** (`UpdatedBy` es el campo de auditoría).
- `POST /api/item {"id":1,...}` con `1` existente → `500` con excepción de SQLite (no `409`).
- Ciclo de `ParentId` (`PUT /api/item/1` con `parentId` de su propio hijo) y después `GET /api/item`
  → la conexión se cierra sin respuesta y **el proceso muere**; los siguientes `GET` reciben
  `Connection refused`. **DoS confirmado** (CWE-674), y la base queda envenenada: tras reiniciar,
  el primer `GET /api/item` vuelve a matar el proceso.

Conclusión: los dos hallazgos críticos del informe son reales y reproducibles, no teóricos.

## Reglas de implementación

1. **Autorizar por defecto**: `[Authorize]` en `BaseController<T>` y en el resto de controladores;
   `[AllowAnonymous]` solo en las rutas de `endpoints.allow-anonymous.txt`.
2. **Registrar la autenticación antes de autorizar**: `AddAuthentication` + `AddJwtBearer` (o
   cookie) en `Program.cs` y `app.UseAuthentication()` **antes** de `app.UseAuthorization()`.
   Hoy solo existe `UseAuthorization()` (`Program.cs:121`) y no hay esquema: ningún `[Authorize]`
   podría satisfacerse.
3. **Roles por acción**, no por controlador: los `override` de los hijos reutilizan la ruta y es
   fácil que un `[Authorize]` a nivel de clase se pierda en una sobrecarga.
4. **El rol se lee del token, nunca del cuerpo**: `UpdatedBy`/`UpdatedBy` se sobrescriben con
   `User.Identity` en el servidor.
5. **Descargas y subidas** heredan el permiso del registro padre (`/file` no debe ser un IDOR).
6. **Test de contrato en CI**: enumerar `EndpointDataSource` y comparar con la lista declarada.

## Decisiones tomadas (Diego, 2026-11-10)

- **Transporte de credencial: JWT.** Access token corto + refresh token; el cliente ya lee
  `access_token` de `localStorage` (`imcui/src/services/api.ts:8`), así que el contrato no cambia.
  El refresh se guarda hasheado en servidor y viaja en cookie `HttpOnly`, nunca el access token.
- **MFA para `superadmin`: segunda iteración.** La primera entrega cubre roles, CRUD de usuarios y
  suplantación; el TOTP y la re-autenticación para acciones sensibles entran después, y hasta
  entonces las acciones de superadmin quedan registradas en la auditoría con actor explícito.
- **`Auth:Enforce`: activo desde el primer despliegue de la Fase 2** (no se publica una API que
  ignora credenciales). El modo aviso se queda solo como herramienta de diagnóstico local.
- **Git: commits locales en `main`, sin push ni merge** hasta que Diego lo pida.
