<template>
  <div class="import-panel">
    <div class="row items-center q-gutter-sm q-mb-md">
      <div class="text-subtitle1 text-weight-medium">Importar desde Excel</div>
      <q-space />
      <q-btn
        v-if="preview"
        flat
        dense
        no-caps
        icon="refresh"
        label="Reanalizar"
        :loading="analysing"
        :disable="!file || importing"
        @click="analyse"
      />
      <q-btn
        v-if="preview || result"
        flat
        dense
        no-caps
        icon="close"
        label="Limpiar"
        :disable="analysing || importing"
        @click="reset"
      />
    </div>

    <!-- Step one: the file. Nothing is sent anywhere until Analizar is pressed. -->
    <div
      class="picker"
      :class="$q.screen.lt.sm ? 'column q-gutter-y-sm' : 'row q-gutter-sm items-start'"
    >
      <div :class="$q.screen.lt.sm ? 'full-width' : 'col'">
        <q-file
          :model-value="file"
          dense
          outlined
          accept=".xlsx,.xls"
          label="Libro de inventario (.xlsx o .xls)"
          hint="Una hoja por grupo raíz. El archivo se lee completo antes de escribir nada."
          :disable="analysing || importing"
          @update:model-value="onFile"
        >
          <template v-slot:prepend><q-icon name="upload_file" /></template>
        </q-file>
        <div v-if="file" class="file-facts text-caption text-grey-7 q-mt-xs">
          <q-icon name="description" size="14px" class="q-mr-xs" />
          <span class="file-name">{{ file.name }}</span>
          <span class="file-size">· {{ formatSize(file.size) }}</span>
        </div>
      </div>
      <div :class="$q.screen.lt.sm ? 'full-width' : ''">
        <q-btn
          unelevated
          no-caps
          color="primary"
          icon="analytics"
          label="Analizar"
          :loading="analysing"
          :disable="!file || importing"
          @click="analyse"
        />
      </div>
    </div>

    <q-banner v-if="errorMessage" dense class="bg-red-1 text-red-9 q-mt-md">
      <template v-slot:avatar><q-icon name="error_outline" /></template>
      {{ errorMessage }}
    </q-banner>

    <q-banner v-else-if="notice" dense class="bg-blue-1 text-blue-10 q-mt-md">
      <template v-slot:avatar><q-icon name="info" /></template>
      {{ notice }}
    </q-banner>

    <q-card v-if="result" flat bordered class="result-card q-mt-md">
      <q-card-section class="q-py-sm">
        <div class="text-subtitle2">Importación terminada</div>
        <div class="text-body2">
          <span class="result-created">{{ result.createdItems }}</span> elemento(s) creados:
          {{ result.createdSheets }} hoja(s) y {{ result.createdGroups }} grupo(s) nuevos.
        </div>
        <div class="result-skipped row q-gutter-x-md text-caption text-grey-8 q-mt-xs">
          <span v-for="fact in resultFacts" :key="fact.label" class="result-fact">
            {{ fact.label }}: <strong>{{ fact.value }}</strong>
          </span>
        </div>
      </q-card-section>
    </q-card>

    <template v-if="preview">
      <div class="summary row items-center q-gutter-x-md q-mt-md text-caption text-grey-8">
        <span>{{ summary?.rows ?? 0 }} filas</span>
        <span>{{ summary?.sections ?? 0 }} secciones</span>
        <span class="text-positive">{{ totals.green }} verdes</span>
        <span class="text-warning">{{ totals.yellow }} amarillos</span>
        <span class="text-negative">{{ totals.red }} rojos</span>
        <span>{{ summary?.importable ?? 0 }} importables</span>
        <span>{{ summary?.skipped ?? 0 }} descartadas</span>
      </div>

      <!-- The filters narrow the view, never the file: what the button sends is counted below the
           tree, over every row, whatever is on screen. -->
      <div
        class="filters q-mt-sm"
        :class="$q.screen.lt.sm ? 'column q-gutter-y-sm' : 'row items-center q-gutter-x-sm'"
      >
        <div
          class="filter-chips row q-gutter-xs"
          :class="$q.screen.lt.sm ? 'column items-start' : ''"
        >
          <q-chip
            clickable
            dense
            class="filter-chip"
            icon="circle"
            :color="onlyRed ? 'negative' : 'grey-4'"
            :text-color="onlyRed ? 'white' : 'dark'"
            :label="`Solo rojos (${totals.red})`"
            @click="onlyRed = !onlyRed"
          />
          <q-chip
            clickable
            dense
            class="filter-chip"
            icon="circle"
            :color="onlyYellow ? 'warning' : 'grey-4'"
            :text-color="onlyYellow ? 'white' : 'dark'"
            :label="`Solo amarillos (${totals.yellow})`"
            @click="onlyYellow = !onlyYellow"
          />
          <q-chip
            clickable
            dense
            class="filter-chip"
            :color="onlyWithCode ? 'primary' : 'grey-4'"
            :text-color="onlyWithCode ? 'white' : 'dark'"
            :label="`Con código (${codeTotals.withCode})`"
            @click="toggleCodeFilter('with')"
          />
          <q-chip
            clickable
            dense
            class="filter-chip"
            :color="onlyWithoutCode ? 'primary' : 'grey-4'"
            :text-color="onlyWithoutCode ? 'white' : 'dark'"
            :label="`Sin código (${codeTotals.withoutCode})`"
            @click="toggleCodeFilter('without')"
          />
        </div>
        <q-input
          v-model="search"
          dense
          outlined
          clearable
          label="Hoja, grupo o elemento"
          class="filter-search"
          :class="$q.screen.lt.sm ? 'full-width' : ''"
        >
          <template v-slot:prepend><q-icon name="search" size="18px" /></template>
        </q-input>
      </div>

      <div v-if="visibleSheets.length === 0" class="no-matches text-caption text-grey-7 q-mt-md">
        Ningún elemento coincide con los filtros.
      </div>

      <div v-else class="import-tree q-mt-sm">
        <div v-for="sheet in visibleSheets" :key="sheet.key" class="import-sheet">
          <div class="sheet-row row items-center no-wrap q-gutter-x-sm">
            <q-icon name="table_chart" size="18px" class="text-grey-7" />
            <span class="sheet-name text-subtitle2">{{ sheet.name }}</span>
            <q-badge :color="statusColour(sheet.counts)" rounded class="status-marker">
              {{ statusLabel(sheet.counts) }}
            </q-badge>
            <span class="level-counts text-caption">
              <span class="count-green text-positive">{{ sheet.counts.green }}</span>
              <span class="count-yellow text-warning">{{ sheet.counts.yellow }}</span>
              <span class="count-red text-negative">{{ sheet.counts.red }}</span>
            </span>
            <q-space />
            <span v-if="sheet.skippedSections > 0" class="text-caption text-grey-7">
              {{ sheet.skippedSections }} títulos de sección
            </span>
          </div>

          <div v-for="group in sheet.groups" :key="group.key" class="import-group">
            <div class="group-row row items-center no-wrap q-gutter-x-sm">
              <q-icon name="folder_open" size="16px" class="text-grey-6" />
              <span class="group-name text-body2 text-weight-medium">{{ group.name }}</span>
              <q-badge :color="statusColour(group.counts)" rounded class="status-marker">
                {{ statusLabel(group.counts) }}
              </q-badge>
              <span class="level-counts text-caption">
                <span class="count-green text-positive">{{ group.counts.green }}</span>
                <span class="count-yellow text-warning">{{ group.counts.yellow }}</span>
                <span class="count-red text-negative">{{ group.counts.red }}</span>
              </span>
            </div>

            <!-- One row of markup, two layouts: a line above `sm`, a card below it. -->
            <div
              v-for="row in group.rows"
              :key="row.item.key"
              class="import-item"
              :class="$q.screen.lt.sm ? 'import-card' : 'import-row'"
            >
              <div class="item-main row items-center no-wrap q-gutter-x-sm">
                <q-checkbox
                  v-if="row.item.status === 'yellow'"
                  dense
                  :model-value="!isSkipped(row)"
                  :disable="!row.item.code || importing"
                  :aria-label="`Importar ${row.item.label}`"
                  @update:model-value="(value: boolean | null) => setIncluded(row, value === true)"
                >
                  <!-- The API keys the exclusion on the code: a row without one cannot be unticked
                       on its own, so the box is disabled and says why instead of lying. -->
                  <q-tooltip v-if="!row.item.code">
                    Sin código no se puede excluir por separado: la importación descarta por código.
                  </q-tooltip>
                </q-checkbox>
                <span v-else class="item-spacer" />

                <q-badge :color="statusColourOf(row.item.status)" rounded class="item-status">
                  {{ STATUS_LABELS[row.item.status] }}
                </q-badge>
                <span class="item-label text-body2">{{ row.item.label || '(sin nombre)' }}</span>
                <span
                  class="item-code text-caption"
                  :class="row.item.code ? 'text-grey-8' : 'text-grey-5'"
                >
                  {{ row.item.code || 'sin código' }}
                </span>
                <span v-if="row.item.brand" class="item-brand text-caption text-grey-7">
                  {{ row.item.brand }}
                </span>
                <q-space />
                <span class="item-row text-caption text-grey-6">fila {{ row.item.row }}</span>
              </div>

              <ul
                v-if="row.item.status !== 'green' && row.item.reasons.length"
                class="item-reasons"
              >
                <li v-for="reason in row.item.reasons" :key="reason">{{ reason }}</li>
              </ul>
            </div>
          </div>
        </div>
      </div>

      <div v-if="preview.normalizations.length" class="normalizations q-mt-md">
        <div class="text-caption text-grey-7">Correcciones aplicadas al leer el archivo</div>
        <ul class="text-caption text-grey-8 q-mt-xs">
          <li v-for="fix in preview.normalizations" :key="fix">{{ fix }}</li>
        </ul>
      </div>

      <div v-if="preview.issues.length" class="issues q-mt-md">
        <div class="text-caption text-grey-7">{{ preview.issues.length }} aviso(s) del archivo</div>
        <ul class="text-caption text-grey-8 q-mt-xs">
          <li v-for="issue in preview.issues" :key="`${issue.sheet}#${issue.row}#${issue.message}`">
            <span class="text-weight-medium">{{ issue.sheet }}</span>
            <span v-if="issue.row > 0"> · fila {{ issue.row }}</span>
            · {{ issue.message }}
          </li>
        </ul>
      </div>

      <div
        class="import-actions row items-center q-mt-md"
        :class="$q.screen.lt.sm ? 'column q-gutter-y-sm' : 'q-gutter-x-sm'"
      >
        <div class="text-caption text-grey-8" :class="$q.screen.lt.sm ? 'full-width' : ''">
          {{ importCount }} de {{ rows.length }} se importarán: los verdes siempre, los amarillos
          marcados. Los rojos no se envían nunca.
        </div>
        <q-space v-if="!$q.screen.lt.sm" />
        <q-btn
          unelevated
          no-caps
          color="primary"
          icon="file_upload"
          class="import-button"
          :class="$q.screen.lt.sm ? 'full-width' : ''"
          :label="importLabel"
          :loading="importing"
          :disable="importCount === 0 || importing || analysing"
          @click="runImport"
        />
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { getErrorMessage } from '@services/api';
import {
  importService,
  type ImportItem,
  type ImportOutcome,
  type ImportPreview,
  type ImportStatus,
} from '@services/import';
import { useFeedback } from '../../composables/useFeedback';

/**
 * The spreadsheet import: a file, the tree it describes with each row coloured, and one button.
 *
 * Preview and import are two calls on purpose (see `ImportController`). The preview writes nothing
 * and is the only thing that decides colours; the import is the user's decision, and it sends the
 * green rows, the yellow rows still ticked, and the codes of the yellow rows that were unticked.
 */

const emit = defineEmits<{ imported: [ImportOutcome] }>();

const feedback = useFeedback();

const STATUS_LABELS: Record<ImportStatus, string> = {
  green: 'verde',
  yellow: 'amarillo',
  red: 'rojo',
};

interface Counts {
  green: number;
  yellow: number;
  red: number;
}

/** One preview row with the names it hangs from, so filtering and counting are a flat pass. */
interface FlatRow {
  sheet: string;
  groupIndex: number;
  group: string;
  item: ImportItem;
}

interface VisibleGroup {
  key: string;
  name: string;
  counts: Counts;
  rows: FlatRow[];
}

interface VisibleSheet {
  key: string;
  name: string;
  skippedSections: number;
  counts: Counts;
  groups: VisibleGroup[];
}

const file = ref<File | null>(null);
const preview = ref<ImportPreview | null>(null);
const result = ref<ImportOutcome | null>(null);
/** How many codes the last run left out at the user's request; the API reports classes, not codes. */
const lastSkipped = ref(0);
const analysing = ref(false);
const importing = ref(false);
const errorMessage = ref<string | null>(null);
const notice = ref<string | null>(null);

/** The item keys the user unticked. Checked is the default; this is the exception list. */
const skippedKeys = ref<string[]>([]);

const onlyRed = ref(false);
const onlyYellow = ref(false);
const onlyWithCode = ref(false);
const onlyWithoutCode = ref(false);
const search = ref('');

const rows = computed<FlatRow[]>(() => {
  const flat: FlatRow[] = [];
  for (const sheet of preview.value?.sheets ?? []) {
    sheet.groups.forEach((group, groupIndex) => {
      for (const item of group.items) {
        flat.push({ sheet: sheet.name, groupIndex, group: group.name, item });
      }
    });
  }
  return flat;
});

const summary = computed(() => preview.value?.summary ?? null);

function emptyCounts(): Counts {
  return { green: 0, yellow: 0, red: 0 };
}

const totals = computed<Counts>(() => {
  const counts = emptyCounts();
  for (const row of rows.value) counts[row.item.status] += 1;
  return counts;
});

const codeTotals = computed(() => {
  let withCode = 0;
  for (const row of rows.value) if (row.item.code) withCode += 1;
  return { withCode, withoutCode: rows.value.length - withCode };
});

function matchesStatus(status: ImportStatus): boolean {
  // Neither chip on: every colour. Both on: the union, which is what a pair of switches means.
  if (!onlyRed.value && !onlyYellow.value) return true;
  return (onlyRed.value && status === 'red') || (onlyYellow.value && status === 'yellow');
}

function matchesCode(item: ImportItem): boolean {
  if (!onlyWithCode.value && !onlyWithoutCode.value) return true;
  return (onlyWithCode.value && !!item.code) || (onlyWithoutCode.value && !item.code);
}

function matchesText(row: FlatRow): boolean {
  const needle = search.value.trim().toLowerCase();
  if (!needle) return true;
  return [row.sheet, row.group, row.item.label].some((value) =>
    value.toLowerCase().includes(needle),
  );
}

const visibleRows = computed<FlatRow[]>(() =>
  rows.value.filter(
    (row) => matchesStatus(row.item.status) && matchesCode(row.item) && matchesText(row),
  ),
);

function skippedSectionsOf(sheetName: string): number {
  return preview.value?.sheets.find((sheet) => sheet.name === sheetName)?.skippedSections ?? 0;
}

/** The tree as it is shown: only the rows that pass the filters, counted as shown. */
const visibleSheets = computed<VisibleSheet[]>(() => {
  const sheets: VisibleSheet[] = [];
  const byKey = new Map<string, VisibleSheet>();

  for (const row of visibleRows.value) {
    const sheetKey = row.sheet;
    let sheet = byKey.get(sheetKey);
    if (!sheet) {
      sheet = {
        key: sheetKey,
        name: row.sheet,
        skippedSections: skippedSectionsOf(row.sheet),
        counts: emptyCounts(),
        groups: [],
      };
      byKey.set(sheetKey, sheet);
      sheets.push(sheet);
    }

    const groupKey = `${sheetKey}##${row.groupIndex}`;
    let group = sheet.groups.find((candidate) => candidate.key === groupKey);
    if (!group) {
      group = { key: groupKey, name: row.group, counts: emptyCounts(), rows: [] };
      sheet.groups.push(group);
    }

    group.rows.push(row);
    group.counts[row.item.status] += 1;
    sheet.counts[row.item.status] += 1;
  }

  return sheets;
});

/** The marker repeats the API's own rule: any red makes the level red, else any yellow, else green. */
function statusOf(counts: Counts): ImportStatus {
  if (counts.red > 0) return 'red';
  if (counts.yellow > 0) return 'yellow';
  return 'green';
}

function statusColour(counts: Counts): string {
  return statusColourOf(statusOf(counts));
}

function statusColourOf(status: ImportStatus): string {
  return status === 'red' ? 'negative' : status === 'yellow' ? 'warning' : 'positive';
}

function statusLabel(counts: Counts): string {
  return STATUS_LABELS[statusOf(counts)];
}

function isSkipped(row: FlatRow): boolean {
  return skippedKeys.value.includes(row.item.key);
}

function setIncluded(row: FlatRow, included: boolean): void {
  const keys = new Set(skippedKeys.value);
  if (included) keys.delete(row.item.key);
  else keys.add(row.item.key);
  skippedKeys.value = [...keys];
}

const yellowSelected = computed(
  () => rows.value.filter((row) => row.item.status === 'yellow' && !isSkipped(row)).length,
);

/** What the button promises: every green row plus the yellow rows still ticked. Reds never count. */
const importCount = computed(
  () => rows.value.filter((row) => row.item.status === 'green').length + yellowSelected.value,
);

const importLabel = computed(() =>
  importCount.value === 1 ? 'Importar 1 elemento' : `Importar ${importCount.value} elementos`,
);

/** The codes of the unticked yellows, deduplicated by the service before they go out. */
const skipCodes = computed<string[]>(() => {
  const codes: string[] = [];
  for (const row of rows.value) {
    if (row.item.status !== 'yellow' || !isSkipped(row)) continue;
    // A yellow row without a code cannot be unticked, so it contributes nothing here.
    if (row.item.code) codes.push(row.item.code);
  }
  return codes;
});

function toggleCodeFilter(which: 'with' | 'without'): void {
  if (which === 'with') onlyWithCode.value = !onlyWithCode.value;
  else onlyWithoutCode.value = !onlyWithoutCode.value;
}

/** The line under the result card, in the order the user reads it. */
const resultFacts = computed<{ label: string; value: number }[]>(() => {
  const outcome = result.value;
  if (!outcome) return [];
  return [
    { label: 'Rojos omitidos', value: outcome.skippedRed },
    { label: 'Amarillos omitidos', value: outcome.skippedYellow },
    { label: 'Ya existían', value: outcome.skippedExisting },
    { label: 'Destildados aquí', value: lastSkipped.value },
  ];
});

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function onFile(value: unknown): void {
  // QFile hands back a FileList when `multiple` is set; this picker takes one book at a time.
  file.value = value instanceof File ? value : null;
  preview.value = null;
  result.value = null;
  lastSkipped.value = 0;
  skippedKeys.value = [];
  errorMessage.value = null;
  notice.value = null;
}

async function analyse(): Promise<void> {
  const picked = file.value;
  if (!picked) return;

  analysing.value = true;
  errorMessage.value = null;
  notice.value = null;
  result.value = null;
  try {
    preview.value = await importService.preview(picked);
    skippedKeys.value = [];
    if (preview.value.summary.rows === 0) {
      notice.value =
        'El archivo se leyó, pero no se reconoció ninguna fila de inventario. Revisa que las hojas tengan el formato esperado.';
    }
  } catch (error) {
    preview.value = null;
    errorMessage.value = getErrorMessage(error);
    feedback.error(error, 'No se pudo analizar el archivo.');
  } finally {
    analysing.value = false;
  }
}

async function runImport(): Promise<void> {
  const picked = file.value;
  if (!picked || importCount.value === 0) return;

  importing.value = true;
  errorMessage.value = null;
  notice.value = null;
  try {
    const codes = skipCodes.value;
    const outcome = await importService.run(picked, {
      // No yellow row still ticked means the whole class is left out, which is what the API's own
      // `skippedYellow` was built to report.
      includeYellow: yellowSelected.value > 0,
      skipCodes: codes,
    });
    result.value = outcome;
    lastSkipped.value = codes.length;
    feedback.success(
      outcome.createdItems === 1
        ? 'Se importó 1 elemento.'
        : `Se importaron ${outcome.createdItems} elementos.`,
    );
    // The inventory page reads the tree when it mounts (`inventory.vue`), so what it shows next is
    // already the post-import tree; the event is for a host that keeps one loaded meanwhile.
    emit('imported', outcome);
  } catch (error) {
    errorMessage.value = getErrorMessage(error);
    feedback.error(error, 'No se pudo importar el archivo.');
  } finally {
    importing.value = false;
  }
}

function reset(): void {
  file.value = null;
  preview.value = null;
  result.value = null;
  lastSkipped.value = 0;
  skippedKeys.value = [];
  errorMessage.value = null;
  notice.value = null;
  search.value = '';
  onlyRed.value = false;
  onlyYellow.value = false;
  onlyWithCode.value = false;
  onlyWithoutCode.value = false;
}
</script>

<style scoped>
.file-facts {
  word-break: break-all;
}

.import-tree {
  border: 1px solid var(--imc-border, rgba(0, 0, 0, 0.12));
  border-radius: 4px;
  overflow: hidden;
}

.import-sheet + .import-sheet {
  border-top: 1px solid var(--imc-border, rgba(0, 0, 0, 0.12));
}

.sheet-row,
.group-row {
  padding: 8px 12px;
}

.sheet-row {
  background: var(--imc-surface-sunken, rgba(0, 0, 0, 0.04));
}

.group-row {
  padding-left: 28px;
}

.level-counts {
  display: inline-flex;
  gap: 8px;
  font-variant-numeric: tabular-nums;
}

.import-group + .import-group {
  border-top: 1px dashed var(--imc-border, rgba(0, 0, 0, 0.08));
}

.import-row {
  display: block;
  padding: 6px 12px 6px 44px;
}

.import-row + .import-row,
.import-card + .import-card {
  border-top: 1px solid var(--imc-border, rgba(0, 0, 0, 0.06));
}

/* Below `sm` each row is a card of its own: same data, one item per block, thumb-sized targets. */
.import-card {
  display: block;
  margin: 8px;
  padding: 10px;
  border: 1px solid var(--imc-border, rgba(0, 0, 0, 0.12));
  border-radius: 4px;
}

.import-card + .import-card {
  border-top: 1px solid var(--imc-border, rgba(0, 0, 0, 0.12));
}

.import-card .item-main {
  flex-wrap: wrap;
  row-gap: 4px;
}

.item-label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.import-card .item-label {
  white-space: normal;
}

.item-spacer {
  display: inline-block;
  width: 40px;
}

.item-reasons {
  margin: 2px 0 0;
  padding-left: 20px;
  color: var(--imc-text-muted, rgba(0, 0, 0, 0.6));
  font-size: 0.75rem;
}

.import-card .item-reasons {
  padding-left: 18px;
}

.import-actions {
  border-top: 1px solid var(--imc-border, rgba(0, 0, 0, 0.08));
  padding-top: 12px;
}

.import-card + .import-actions {
  border-top: none;
}
</style>
