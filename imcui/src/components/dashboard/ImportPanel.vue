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
        :disable="!file || importing || saving"
        @click="analyse"
      />
      <q-btn
        v-if="preview || result"
        flat
        dense
        no-caps
        icon="close"
        label="Limpiar"
        :disable="analysing || importing || saving"
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
          accept=".xlsx,.xls,.csv"
          label="Inventario (.xlsx, .xls o .csv)"
          hint="Una categoría por grupo raíz. El archivo se lee completo antes de escribir nada."
          :disable="analysing || importing || saving"
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
          :disable="!file || importing || saving"
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

    <!-- A refused correction must not vanish: a red row that stays red looks like a button that
         did nothing. The server says which line it refused and why. -->
    <q-banner v-if="correctionIssues.length" dense class="corrections-warning q-mt-md">
      <template v-slot:avatar><q-icon name="report_problem" /></template>
      <div class="text-caption text-weight-medium">El servidor no aplicó estas correcciones</div>
      <ul class="corrections-list">
        <li v-for="issue in correctionIssues" :key="issue">{{ issue }}</li>
      </ul>
    </q-banner>

    <q-card v-if="result" flat bordered class="result-card q-mt-md">
      <q-card-section class="q-py-sm">
        <div class="text-subtitle2">Importación terminada</div>
        <div class="text-body2">
          <span class="result-created">{{ result.createdItems }}</span> elemento(s) creados:
          {{ result.createdSheets }} categoría(s) y {{ result.createdGroups }} grupo(s) nuevos.
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
          label="Categoría, grupo o elemento"
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
            <div
              class="group-row row items-center no-wrap q-gutter-x-sm"
              :class="$q.screen.lt.sm ? 'group-row--wrap' : ''"
            >
              <q-icon name="folder_open" size="16px" class="text-grey-6" />
              <span class="group-name text-body2 text-weight-medium">{{ group.name }}</span>
              <!-- The proposal is a chip, not another sentence: it has to be seen before the button
                   is pressed, and a note reads like every other explanation. Warning, never
                   negative — the rows are placed where the header says; this only says "look". -->
              <q-chip
                v-if="group.suggestion"
                dense
                class="suggestion-chip"
                :label="suggestionLabel(group.suggestion)"
              />
              <q-badge :color="statusColour(group.counts)" rounded class="status-marker">
                {{ statusLabel(group.counts) }}
              </q-badge>
              <span class="level-counts text-caption">
                <span class="count-green text-positive">{{ group.counts.green }}</span>
                <span class="count-yellow text-warning">{{ group.counts.yellow }}</span>
                <span class="count-red text-negative">{{ group.counts.red }}</span>
              </span>
            </div>

            <!-- A group that a rule placed carries one line saying why: full text, it is the
                 explanation, not a label to be shortened. -->
            <div v-if="group.note" class="group-note text-caption">
              {{ group.note }}
            </div>

            <!-- One row of markup, two layouts: a line above `sm`, a card below it. A red or yellow
                 row can be corrected before importing, so tapping it opens the editor; a green row
                 is already importable and opens nothing. -->
            <div
              v-for="row in group.rows"
              :key="row.item.key"
              class="import-item"
              :class="[
                $q.screen.lt.sm ? 'import-card' : 'import-row',
                isEditable(row) ? 'import-item--editable' : '',
              ]"
              :role="isEditable(row) ? 'button' : undefined"
              :tabindex="isEditable(row) ? 0 : undefined"
              :aria-label="isEditable(row) ? `Corregir la fila ${row.item.row}` : undefined"
              @click="openEditor(row)"
              @keydown.enter.prevent="openEditor(row)"
              @keydown.space.prevent="openEditor(row)"
            >
              <div class="item-main row items-center no-wrap q-gutter-x-sm">
                <q-checkbox
                  v-if="row.item.status === 'yellow'"
                  dense
                  :model-value="!isSkipped(row)"
                  :disable="!row.item.code || importing || saving"
                  :aria-label="`Importar ${row.item.label}`"
                  @click.stop
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
                <!-- What the user touched, so the tree says which rows no longer say what the file
                     says. The chip is the mark; the undo button is the way back. -->
                <q-chip
                  v-if="isCorrected(row.item)"
                  dense
                  icon="edit"
                  label="Corregido"
                  class="corrected-chip bg-blue-grey-1 text-blue-grey-9"
                />
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
                <q-btn
                  v-if="isCorrected(row.item)"
                  flat
                  dense
                  round
                  icon="undo"
                  class="undo-correction"
                  :aria-label="`Deshacer la corrección de la fila ${row.item.row}`"
                  :disable="saving || importing"
                  @click.stop="undoCorrection(row.item)"
                />
                <q-icon
                  v-else-if="isEditable(row)"
                  name="edit"
                  size="16px"
                  class="edit-hint text-grey-7"
                  aria-hidden="true"
                />
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
          :disable="importCount === 0 || importing || analysing || saving"
          @click="runImport"
        />
      </div>
    </template>

    <!-- The row editor. Two halves: what the file says on top (its own header row and the row
         itself, in the file's column order), and what would be imported below, editable. Only a
         red or yellow row reaches it. Full screen below `sm`, where a spreadsheet row does not fit
         a phone and is scrolled sideways instead. -->
    <q-dialog
      v-model="editorOpen"
      :maximized="$q.screen.lt.sm"
      transition-show="slide-up"
      transition-hide="slide-down"
    >
      <q-card v-if="editor" class="editor-card column no-wrap">
        <q-card-section class="editor-head row items-center no-wrap q-gutter-x-sm q-py-sm">
          <q-icon name="edit_note" size="22px" class="text-grey-7" />
          <div class="editor-heading column">
            <span class="text-subtitle1 text-weight-medium">Corregir la fila {{ editor.row }}</span>
            <span class="text-caption text-grey-7">
              <span v-if="editor.source">{{ editor.source }} ·</span>
              se importará en {{ editor.category }} › {{ editor.group }}
            </span>
          </div>
          <q-space />
          <q-btn flat dense round icon="close" aria-label="Cerrar el editor" @click="closeEditor" />
        </q-card-section>

        <div class="editor-divider" />

        <q-card-section class="editor-body">
          <div class="editor-label text-caption text-grey-7">El archivo dice</div>
          <div class="file-sheet-scroll">
            <table class="file-sheet">
              <thead>
                <tr>
                  <th v-for="header in editor.headers" :key="header" class="file-header">
                    {{ header }}
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td v-for="header in editor.headers" :key="header" class="file-cell">
                    {{ editor.values[header] ?? '' }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="editor-label text-caption text-grey-7 q-mt-md">
            Lo que se importará · editable
          </div>
          <div class="editor-form">
            <q-input
              v-model="editor.draft.label"
              dense
              outlined
              label="Descripción"
              hint="Obligatoria: sin descripción la fila no se puede importar."
              :error="editorTouched && !editor.draft.label.trim()"
              error-message="Escribe una descripción para poder importar la fila."
              class="editor-description"
            />
            <q-input v-model="editor.draft.code" dense outlined label="Código" />
            <q-input
              v-model="editor.draft.subcategory"
              dense
              outlined
              label="Subcategoría"
              hint="El nombre con el que se colocará en el árbol."
            />
            <q-input v-model="editor.draft.brand" dense outlined label="Marca" />
            <q-input v-model="editor.draft.reference" dense outlined label="Referencia" />
            <q-input v-model="editor.draft.serial" dense outlined label="Serial" />
            <q-input v-model="editor.draft.notes" dense outlined label="Notas" />
            <q-input v-model="editor.draft.dimensions" dense outlined label="Dimensiones" />
          </div>
        </q-card-section>

        <div class="editor-divider" />

        <q-card-section class="editor-actions row items-center q-gutter-x-sm q-py-sm">
          <q-btn
            v-if="editor.corrected"
            flat
            dense
            no-caps
            icon="undo"
            label="Deshacer"
            class="undo-editor"
            :disable="saving"
            @click="undoEditor"
          />
          <q-space />
          <q-btn flat no-caps label="Cancelar" @click="closeEditor" />
          <q-btn
            unelevated
            no-caps
            color="primary"
            label="Guardar y revisar"
            class="save-correction"
            :loading="saving"
            :disable="!editor.draft.label.trim() || saving"
            @click="saveEditor"
          />
        </q-card-section>
      </q-card>
    </q-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { getErrorMessage } from '@services/api';
import {
  importService,
  subcategoryFromRaw,
  descriptionFromRaw,
  type ImportCorrection,
  type ImportItem,
  type ImportOutcome,
  type ImportPreview,
  type ImportRequest,
  type ImportStatus,
  type ImportSuggestion,
} from '@services/import';
import { useFeedback } from '../../composables/useFeedback';

/**
 * The spreadsheet import: a file, the tree it describes with each row coloured, and one button.
 *
 * Preview and import are two calls on purpose (see `ImportController`). The preview writes nothing
 * and is the only thing that decides colours; the import is the user's decision, and it sends the
 * green rows, the yellow rows still ticked, and the codes of the yellow rows that were unticked.
 *
 * A red or yellow row can be corrected before it is imported. The editor shows the file's own row
 * on top and the item as it would be imported below; saving posts a new preview that carries every
 * correction made so far, so the tree stays the server's answer and never a local guess. The same
 * list rides along on the import, because the write must be the sum of what the user fixed.
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
  /** The group's own explanation, when the API placed it by a rule rather than by its name. */
  groupNote: string | null;
  /** The similarity proposal behind that explanation, when there is one. */
  groupSuggestion: ImportSuggestion | null;
  item: ImportItem;
}

interface VisibleGroup {
  key: string;
  name: string;
  note: string | null;
  suggestion: ImportSuggestion | null;
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

/**
 * The editable half of the editor: one string per field the API accepts as a correction. Everything
 * is a string because it comes from a spreadsheet cell and it is typed back into an input; the
 * payload drops the empty ones instead of guessing a null.
 */
interface Draft {
  source: string;
  row: number;
  label: string;
  code: string;
  subcategory: string;
  brand: string;
  reference: string;
  serial: string;
  notes: string;
  dimensions: string;
}

/** The open editor: the item it corrects, the file's row to show, and the fields being typed. */
interface Editor {
  key: string;
  row: number;
  source: string;
  category: string;
  group: string;
  /** Whether this row already carries a correction, which is what the undo button keys on. */
  corrected: boolean;
  /** The file's header row for this item's sheet, in the file's column order. */
  headers: string[];
  /** The file's values, keyed by that same header. An absent cell reads as empty. */
  values: Record<string, string>;
  draft: Draft;
}

const file = ref<File | null>(null);
const preview = ref<ImportPreview | null>(null);
const result = ref<ImportOutcome | null>(null);
/** How many codes the last run left out at the user's request; the API reports classes, not codes. */
const lastSkipped = ref(0);
const analysing = ref(false);
const importing = ref(false);
/** A preview is being re-run with a correction; the editor and the tree both wait for it. */
const saving = ref(false);
const errorMessage = ref<string | null>(null);
const notice = ref<string | null>(null);

/** The item keys the user unticked. Checked is the default; this is the exception list. */
const skippedKeys = ref<string[]>([]);

/**
 * Every correction made in this session, keyed by `source|row` — the pair the API needs, because a
 * row number alone repeats across the file's sheets. The value is the correction itself, which is
 * what both the next preview and the import send.
 */
const corrections = ref<Record<string, ImportCorrection>>({});

const editorOpen = ref(false);
/** The open editor, `null` when there is none. A copy: the tree may be replaced under it. */
const editor = ref<Editor | null>(null);
/** The description is required: its error only shows once the user tried to save without one. */
const editorTouched = ref(false);

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
        flat.push({
          sheet: sheet.name,
          groupIndex,
          group: group.name,
          groupNote: group.note ?? null,
          groupSuggestion: group.suggestion ?? null,
          item,
        });
      }
    });
  }
  return flat;
});

const summary = computed(() => preview.value?.summary ?? null);

/**
 * Every correction the server refused, from the preview and from the import, without repeating one
 * that both said. A refused correction that nobody renders looks like a button that did nothing.
 */
const correctionIssues = computed<string[]>(() => {
  const issues = new Set<string>();
  for (const issue of preview.value?.correctionIssues ?? []) issues.add(issue);
  for (const issue of result.value?.correctionIssues ?? []) issues.add(issue);
  return [...issues];
});

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
      group = {
        key: groupKey,
        name: row.group,
        note: row.groupNote,
        suggestion: row.groupSuggestion,
        counts: emptyCounts(),
        rows: [],
      };
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

/**
 * The chip's whole text. It says *sugerencia* on purpose: the rows are already where the header
 * points, so the reader must not think the import renamed anything.
 */
function suggestionLabel(suggestion: ImportSuggestion): string {
  return `Sugerencia · ${suggestion.percent}%`;
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

/* --------------------------------------------------------------------------------------------
 * Correcting a row
 *
 * The server decides; the panel asks. Saving posts a new preview with every correction made so far
 * and re-renders the tree it answers with, so a red row that becomes importable is green because
 * the server said so, not because the panel hoped it would. `source|row` is the identity: the same
 * line number exists in more than one sheet, and a correction without its sheet is ambiguous.
 * ------------------------------------------------------------------------------------------ */

/** The identity of a row across the file, and the key of its correction. */
function correctionKey(source: string | null | undefined, row: number): string {
  return `${(source ?? '').trim()}|${row}`;
}

/** A green row is already importable: it has nothing to correct, so it opens nothing. */
function isEditable(row: FlatRow): boolean {
  return row.item.status !== 'green';
}

function isCorrected(item: ImportItem): boolean {
  return correctionKey(item.source, item.row) in corrections.value;
}

/** The file's header row for this item, in the file's own column order. */
function fileHeadersOf(item: ImportItem): string[] {
  const declared = preview.value?.sourceHeaders?.[item.source ?? ''] ?? [];
  const headers = declared.length > 0 ? declared : Object.keys(item.raw ?? {});
  // An unnamed column has no label to print; its value still has its place in the file's order.
  return headers.filter((header) => header.trim().length > 0);
}

/** The corrections, in a stable order, so two identical payloads look identical. */
function sortedCorrections(list: ImportCorrection[]): ImportCorrection[] {
  return [...list].sort(
    (left, right) => (left.source ?? '').localeCompare(right.source ?? '') || left.row - right.row,
  );
}

/** Every correction made so far, ordered: what the next preview and the import both send. */
const correctionList = computed(() => sortedCorrections(Object.values(corrections.value)));

/**
 * The draft starts from what the server already answered for the row — so a corrected row reopens
 * with its correction — and falls back to the file only for the subcategory, which the item does
 * not carry: the API reports where the row was placed, not the cell the file wrote.
 */
function draftOf(item: ImportItem): Draft {
  const saved = corrections.value[correctionKey(item.source, item.row)];
  return {
    source: item.source ?? '',
    row: item.row,
    label: saved?.label ?? descriptionFromRaw(item) ?? item.label,
    code: saved?.code ?? item.code ?? '',
    subcategory: saved?.subcategory ?? subcategoryFromRaw(item),
    brand: saved?.brand ?? item.brand ?? '',
    reference: saved?.reference ?? item.reference ?? '',
    serial: saved?.serial ?? item.serial ?? '',
    notes: saved?.notes ?? item.notes ?? '',
    dimensions: saved?.dimensions ?? item.dimensions ?? '',
  };
}

function openEditor(row: FlatRow): void {
  if (!isEditable(row)) return;
  const item = row.item;
  const key = correctionKey(item.source, item.row);
  editor.value = {
    key,
    row: item.row,
    source: item.source ?? '',
    category: row.sheet,
    group: row.group,
    corrected: key in corrections.value,
    headers: fileHeadersOf(item),
    values: item.raw ?? {},
    draft: draftOf(item),
  };
  editorTouched.value = false;
  editorOpen.value = true;
}

function closeEditor(): void {
  editorOpen.value = false;
  editor.value = null;
  editorTouched.value = false;
}

/**
 * Re-runs the preview with exactly this list of corrections and keeps the server's answer. Returns
 * whether it landed: the editor only closes on a real answer.
 */
async function applyCorrections(list: ImportCorrection[], message: string): Promise<boolean> {
  const picked = file.value;
  if (!picked) return false;

  saving.value = true;
  errorMessage.value = null;
  try {
    const ordered = sortedCorrections(list);
    preview.value = await importService.preview(picked, ordered);

    const next: Record<string, ImportCorrection> = {};
    for (const correction of ordered) {
      next[correctionKey(correction.source, correction.row)] = correction;
    }
    corrections.value = next;

    // A corrected subcategory can move a row to another category, which changes its key: a stale
    // skipped key would keep a row unticked in a group that no longer holds it.
    const live = new Set(rows.value.map((row) => row.item.key));
    skippedKeys.value = skippedKeys.value.filter((key) => live.has(key));

    feedback.success(message);
    return true;
  } catch (error) {
    errorMessage.value = getErrorMessage(error);
    feedback.error(error, 'No se pudo revisar la corrección.');
    return false;
  } finally {
    saving.value = false;
  }
}

/** Saves the row being edited: one correction, plus the ones already made in this session. */
async function saveEditor(): Promise<void> {
  const current = editor.value;
  if (!current || saving.value) return;

  const draft = current.draft;
  // The description is what makes a red row importable; an empty one is refused by the server, so
  // the editor refuses to send it first.
  if (!draft.label.trim()) {
    editorTouched.value = true;
    return;
  }

  const next: Record<string, ImportCorrection> = {
    ...corrections.value,
    [current.key]: correctionOf(draft),
  };

  if (
    await applyCorrections(
      Object.values(next),
      'Corrección guardada: la fila se vuelve a analizar.',
    )
  )
    closeEditor();
}

/**
 * The correction a draft describes: trimmed, and with every empty field **left out**, because an
 * absent field keeps what the file says while an empty string would clear the cell.
 */
function correctionOf(draft: Draft): ImportCorrection {
  const correction: ImportCorrection = { row: draft.row };
  const source = draft.source.trim();
  if (source) correction.source = source;

  const fields = [
    'label',
    'code',
    'subcategory',
    'brand',
    'reference',
    'serial',
    'notes',
    'dimensions',
  ] as const;
  for (const field of fields) {
    const value = draft[field].trim();
    if (value) correction[field] = value;
  }

  return correction;
}

/**
 * Back to the file's value, one button, no confirmation: the correction is dropped and the preview
 * is asked again without it, so the tree cannot keep showing an edit the server no longer has.
 */
async function undoCorrection(item: ImportItem): Promise<void> {
  const key = correctionKey(item.source, item.row);
  if (saving.value || !(key in corrections.value)) return;

  const next = { ...corrections.value };
  delete next[key];

  if (editor.value?.key === key) closeEditor();
  await applyCorrections(
    Object.values(next),
    'Se deshizo la corrección: la fila vuelve a como está en el archivo.',
  );
}

/** The editor's own undo: the same correction, dropped, with the editor closing behind it. */
async function undoEditor(): Promise<void> {
  const current = editor.value;
  if (!current) return;
  closeEditor();
  const next = { ...corrections.value };
  if (current.key in next) delete next[current.key];
  await applyCorrections(
    Object.values(next),
    'Se deshizo la corrección: la fila vuelve a como está en el archivo.',
  );
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
    { label: 'Correcciones aplicadas', value: outcome.corrected },
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
  // A different file has different rows: a correction keyed to the old one would point at nothing.
  corrections.value = {};
  closeEditor();
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
  closeEditor();
  try {
    // Re-analysing the same file keeps what the user fixed: the tree is the sum of their edits, so
    // the corrections go out with the file every time.
    preview.value = await importService.preview(picked, correctionList.value);
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
    const request: ImportRequest = {
      // No yellow row still ticked means the whole class is left out, which is what the API's own
      // `skippedYellow` was built to report.
      includeYellow: yellowSelected.value > 0,
      skipCodes: codes,
    };
    // The write must be the sum of what the user fixed: without this the import would recreate the
    // rows the file describes and quietly drop every correction the tree is showing.
    if (correctionList.value.length > 0) request.corrections = correctionList.value;

    const outcome = await importService.run(picked, request);
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
  corrections.value = {};
  closeEditor();
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

/* The name is the one thing in the header allowed to shrink: a long one wraps inside the panel
   instead of pushing the badge and the counts out of it at 390px. */
.group-name {
  min-width: 0;
  overflow-wrap: anywhere;
}

/* Below `sm` the header wraps like the item cards do: the suggestion chip drops to a second line
   with the name instead of forcing the row wider than the panel. */
.group-row--wrap {
  flex-wrap: wrap;
  row-gap: 4px;
}

/* The similarity proposal: the warning pair from the theme tokens, so it reads as "you can, look
   first" in both modes and never as an error. It is a chip so the eye finds it, the note explains. */
.suggestion-chip {
  background: var(--imc-warning-bg);
  color: var(--imc-warning-text);
  font-weight: 500;
  white-space: nowrap;
}

/* The one line that explains a rule-made placement. It sits under the header, aligned with the
   group name (28px padding + 16px icon + 8px gutter), and wraps instead of truncating: it is the
   reasoning, so losing its end loses the answer. */
.group-note {
  padding: 0 12px 6px 52px;
  color: var(--imc-text-muted, rgba(0, 0, 0, 0.6));
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

/* A red or yellow row opens the editor, so it has to look tappable: a pointer cursor, a hint of a
   surface on hover, and a ring for the keyboard, which is the only affordance a card cannot fake. */
.import-item--editable {
  cursor: pointer;
}

.import-item--editable:hover,
.import-item--editable:focus-visible {
  background: var(--imc-surface-raised, rgba(0, 0, 0, 0.03));
}

.import-item--editable:focus-visible {
  outline: 2px solid var(--q-primary, #1976d2);
  outline-offset: -2px;
}

.corrected-chip {
  font-weight: 500;
  white-space: nowrap;
}

/* The refused corrections: the warning pair from the theme tokens, so it reads the same in both
   modes and never disappears into the page. */
.corrections-warning {
  background: var(--imc-warning-bg);
  color: var(--imc-warning-text);
}

.corrections-list {
  margin: 4px 0 0;
  padding-left: 20px;
  font-size: 0.75rem;
}

/* The editor. A sheet on top, a form below, both inside one scrolling body: the header and the
   actions stay put so the row's identity and the way out are always on screen. The height is
   explicit because the dialog's own inner box has no height to inherit on a desktop. */
.editor-card {
  width: 100%;
  max-width: 860px;
  height: calc(100vh - 48px);
  max-height: 760px;
}

/* Below `sm` the dialog is maximized: the card takes the whole screen and only the body scrolls. */
@media (max-width: 599px) {
  .editor-card {
    height: 100vh;
    max-height: 100vh;
  }
}

.editor-head {
  min-height: 52px;
}

.editor-heading {
  min-width: 0;
  overflow-wrap: anywhere;
}

.editor-divider {
  border-top: 1px solid var(--imc-border, rgba(0, 0, 0, 0.12));
}

.editor-body {
  flex: 1 1 auto;
  min-height: 0;
  overflow: auto;
  padding: 12px 16px;
}

.editor-label {
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-weight: 600;
}

/* A spreadsheet row is allowed to be wider than a phone: the table keeps its column widths and the
   block scrolls sideways instead of squeezing every cell into 390px. */
.file-sheet-scroll {
  overflow-x: auto;
  border: 1px solid var(--imc-border, rgba(0, 0, 0, 0.12));
  border-radius: 4px;
}

.file-sheet {
  width: max-content;
  min-width: 100%;
  border-collapse: collapse;
  font-size: 0.75rem;
}

.file-header,
.file-cell {
  padding: 6px 10px;
  text-align: left;
  white-space: nowrap;
  border-bottom: 1px solid var(--imc-hairline, rgba(0, 0, 0, 0.08));
}

.file-header {
  background: var(--imc-surface-raised, rgba(0, 0, 0, 0.04));
  color: var(--imc-text-muted, rgba(0, 0, 0, 0.6));
  font-size: 0.68rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.file-cell {
  background: var(--imc-surface);
  color: var(--imc-text-body, rgba(0, 0, 0, 0.87));
}

.editor-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-width: 560px;
}

.editor-actions {
  background: var(--imc-surface);
}
</style>
