# AI Inventory — InventoryManagementContainers

Generated 2026-11-10 11:42 UTC by `code-inventory` / `ai_inventory.py`. Do not hand-edit.

This index routes you to 18 module documents. Indexed: **743 symbols** across **84 files**.

## How to use this file — mandatory protocol

You are an AI agent working in this repository. This index is the map; the module files are the territory. Follow these rules in order, they exist because skipping them is expensive:

1. **Start here, not in the code.** Read this index, pick the module below that owns your change, and open **only** that module file. Loading every module is the same mistake as reading the whole repo.
2. **Then the range, not the file.** Every symbol carries `path:line`. Open that range plus ~10 lines of context. Do not open a 700-line controller to look at one method.
3. **Reuse before writing.** Search the module for the symbol, route or DTO you need. If it exists, extend or call it; a second implementation of the same concern is a defect.
4. **Read Impact before changing a signature.** Every caller listed must be updated in the same change, including callers in other modules (they carry their own `path:line`). Walk one level at a time for transitive impact.
5. **Never copy the inventory or a source file into the conversation as "context".** Cite `path:line` instead and read what you actually need. The document is a map, quoted into the prompt it becomes dead weight that is re-sent on every turn.
6. **One symbol, one question? Use the CLI, not a document:**

```bash
python3 <skill>/scripts/ai_inventory.py --root . --context SYMBOL_NAME --budget 8000
```

7. **Absence is not proof.** Dynamic dispatch, reflection, generated code and unsupported languages are not indexed. Grep before concluding that something is unused.

## Modules

- **IMCAPI** — 1 file, 4 symbols, 4 KB → [`inventory/IMCAPI.md`](inventory/IMCAPI.md) · `Program`, `Main`, `AddRolePolicy`, `AddRolePolicy`
- **IMCAPI/Controllers** — 6 files, 59 symbols, 20 KB → [`inventory/IMCAPI-Controllers.md`](inventory/IMCAPI-Controllers.md) · `AuditController`, `Get`, `AuthController`, `.ctor`, `.ctor`
- **IMCAPI/DB** — 2 files, 20 symbols, 8 KB → [`inventory/IMCAPI-DB.md`](inventory/IMCAPI-DB.md) · `ApplicationContext`, `Items`, `Attachments`, `Constants`, `RefreshTokens`
- **IMCAPI/DB/Entities** — 5 files, 103 symbols, 6 KB → [`inventory/IMCAPI-DB-Entities.md`](inventory/IMCAPI-DB-Entities.md) · `AuditLog`, `Id`, `TimestampUtc`, `ActorUserId`, `ActorEmail`
- **IMCAPI/DB/Repositories** — 1 file, 24 symbols, 12 KB → [`inventory/IMCAPI-DB-Repositories.md`](inventory/IMCAPI-DB-Repositories.md) · `IRepository`, `GetAll`, `GetById`, `GetAllByCondition`, `Add`
- **IMCAPI/Migrations** — 13 files, 26 symbols, 9 KB → [`inventory/IMCAPI-Migrations.md`](inventory/IMCAPI-Migrations.md) · `Initial`, `BuildTargetModel`, `Up`, `Down`, `AttachmentThumbnail`
- **IMCAPI/Services** — 4 files, 48 symbols, 31 KB → [`inventory/IMCAPI-Services.md`](inventory/IMCAPI-Services.md) · `AttachmentStorage`, `.ctor`, `StoredAttachment`, `ApplyUploadAsync`, `SaveAsync`
- **Testing** — 8 files, 117 symbols, 33 KB → [`inventory/Testing.md`](inventory/Testing.md) · `ApiFactory`, `.cctor`, `DatabasePath`, `MediaRoot`, `CreateClient`
- **imcui** — 1 file, 2 symbols, 2 KB → [`inventory/imcui.md`](inventory/imcui.md) · `ImportMetaEnv`, `ImportMeta`
- **imcui/src** — 2 files, 8 symbols, 3 KB → [`inventory/imcui-src.md`](inventory/imcui-src.md) · `App`, `MemoryStorage`, `length`, `clear`, `getItem`
- **imcui/src-pwa** — 1 file, 3 symbols, 3 KB → [`inventory/imcui-src-pwa.md`](inventory/imcui-src-pwa.md) · `updated`, `offline`, `error`
- **imcui/src/boot** — 1 file, 1 symbols, 3 KB → [`inventory/imcui-src-boot.md`](inventory/imcui-src-boot.md) · `handler`
- **imcui/src/components** — 10 files, 99 symbols, 15 KB → [`inventory/imcui-src-components.md`](inventory/imcui-src-components.md) · `AttachmentRow`, `onClick`, `default`, `render`, `row`
- **imcui/src/composables** — 3 files, 46 symbols, 12 KB → [`inventory/imcui-src-composables.md`](inventory/imcui-src-composables.md) · `useFeedback`, `success`, `error`, `info`, `confirmDelete`
- **imcui/src/pages** — 7 files, 53 symbols, 8 KB → [`inventory/imcui-src-pages.md`](inventory/imcui-src-pages.md) · `[...path]`, `index`, `Section`, `sections`, `rolesText`
- **imcui/src/router** — 3 files, 12 symbols, 6 KB → [`inventory/imcui-src-router.md`](inventory/imcui-src-router.md) · `GuardContext`, `GuardDecision`, `normalizePath`, `isPublic`, `requiredRoles`
- **imcui/src/services** — 12 files, 97 symbols, 34 KB → [`inventory/imcui-src-services.md`](inventory/imcui-src-services.md) · `jsonResponse`, `json`, `json`, `ApiError`, `constructor`
- **imcui/src/stores** — 4 files, 21 symbols, 6 KB → [`inventory/imcui-src-stores.md`](inventory/imcui-src-stores.md) · `jsonResponse`, `json`, `emptyResponse`, `json`, `makeToken`

## Cross-module calls (who depends on whom)

- `IMCAPI/Controllers` → `IMCAPI/Services` — 35 calls
- `Testing` → `IMCAPI/Services` — 17 calls
- `imcui/src/components` → `imcui/src` — 15 calls
- `IMCAPI/Controllers` → `IMCAPI/DB` — 11 calls
- `IMCAPI` → `IMCAPI/DB` — 9 calls
- `imcui/src/pages` → `imcui/src` — 9 calls
- `imcui/src/stores` → `imcui/src` — 7 calls
- `IMCAPI/DB` → `IMCAPI/Services` — 4 calls
- `Testing` → `IMCAPI/DB` — 4 calls
- `imcui/src/composables` → `imcui/src` — 1 call
- `imcui/src/router` → `imcui/src` — 1 call

## Coverage & caveats

- 689 in-project call edges of 6959 call sites; the rest are framework/dynamic.
- Purposes come from doc comments only; blank means none. Absence of a symbol is not proof it does not exist (dynamic dispatch, generated code and unsupported languages are not indexed).

## Regenerate

```bash
python3 <skill>/scripts/inventory.py sync --root . --lang py   # or cs / js
python3 <skill>/scripts/ai_inventory.py --root .
```
