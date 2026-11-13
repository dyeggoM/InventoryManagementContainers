<template>
  <div class="archive-panel">
    <div class="row items-center q-gutter-sm q-mb-md">
      <div class="text-subtitle1 text-weight-medium">Archivo</div>
      <q-space />
      <q-btn flat dense no-caps icon="refresh" label="Recargar" :loading="loading" @click="load" />
    </div>

    <div
      class="filters q-mb-md"
      :class="$q.screen.lt.sm ? 'column q-gutter-y-sm' : 'row q-col-gutter-sm'"
    >
      <div :class="$q.screen.lt.sm ? 'full-width' : 'col-12 col-sm-6 col-md-3'">
        <q-input v-model="filters.label" dense outlined label="Etiqueta" clearable>
          <template v-slot:prepend><q-icon name="label" size="18px" /></template>
        </q-input>
      </div>
      <div :class="$q.screen.lt.sm ? 'full-width' : 'col-12 col-sm-6 col-md-3'">
        <q-input v-model="filters.actor" dense outlined label="Eliminado por" clearable>
          <template v-slot:prepend><q-icon name="person" size="18px" /></template>
        </q-input>
      </div>
      <div :class="$q.screen.lt.sm ? 'full-width' : 'col-12 col-sm-6 col-md-2'">
        <q-input v-model="filters.from" dense outlined label="Desde" type="date" clearable />
      </div>
      <div :class="$q.screen.lt.sm ? 'full-width' : 'col-12 col-sm-6 col-md-2'">
        <q-input v-model="filters.to" dense outlined label="Hasta" type="date" clearable />
      </div>
      <!-- The buttons take a row of their own on a phone instead of squeezing into the last column. -->
      <div
        class="filter-actions row items-center"
        :class="$q.screen.lt.sm ? 'full-width q-gutter-x-sm' : 'col-12 col-sm-6 col-md-2 q-gutter-xs'"
      >
        <q-btn unelevated no-caps color="primary" icon="search" label="Buscar" @click="apply" />
        <q-btn flat no-caps label="Limpiar" @click="clear" />
      </div>
    </div>

    <q-banner v-if="notice" dense class="bg-blue-1 text-blue-10 q-mb-md">
      <template v-slot:avatar><q-icon name="info" /></template>
      {{ notice }}
    </q-banner>

    <q-banner v-if="errorMessage" dense class="bg-red-1 text-red-9 q-mb-md">
      <template v-slot:avatar><q-icon name="error_outline" /></template>
      {{ errorMessage }}
    </q-banner>

    <q-table
      v-if="!$q.screen.lt.sm"
      :rows="rows"
      :columns="columns"
      row-key="archiveId"
      dense
      flat
      bordered
      :loading="loading"
      :rows-per-page-options="[0]"
      no-data-label="No hay registros en el archivo"
    >
      <template v-slot:body-cell-label="props">
        <q-td :props="props">
          <div class="row items-center q-gutter-xs no-wrap">
            <q-icon :name="props.row.icon || 'inventory_2'" size="18px" class="text-grey-7" />
            <span class="text-body2">{{ props.row.label }}</span>
            <span class="text-caption text-grey-7">#{{ props.row.itemId }}</span>
          </div>
        </q-td>
      </template>

      <template v-slot:body-cell-deletedAtUtc="props">
        <q-td :props="props">{{ formatTimestamp(props.row.deletedAtUtc) }}</q-td>
      </template>

      <template v-slot:body-cell-deletedBy="props">
        <q-td :props="props">{{ props.row.deletedBy || '—' }}</q-td>
      </template>

      <template v-slot:body-cell-attachments="props">
        <q-td :props="props">
          <q-icon
            v-if="props.row.attachmentsJson !== null"
            name="attach_file"
            size="18px"
            class="text-grey-8"
            aria-label="Tenía adjuntos"
          >
            <q-tooltip>Tenía adjuntos archivados</q-tooltip>
          </q-icon>
          <span v-else class="text-grey-5">—</span>
        </q-td>
      </template>

      <template v-slot:body-cell-state="props">
        <q-td :props="props">
          <q-badge :color="props.row.restoredAtUtc !== null ? 'positive' : 'grey-6'" rounded>
            {{ props.row.restoredAtUtc !== null ? 'Restaurado' : 'En archivo' }}
          </q-badge>
        </q-td>
      </template>

      <template v-slot:body-cell-actions="props">
        <q-td :props="props" class="text-right">
          <q-btn
            flat
            dense
            no-caps
            icon="restore"
            label="Restaurar"
            :loading="busyId === props.row.archiveId"
            :disable="props.row.restoredAtUtc !== null"
            :aria-label="`Restaurar ${props.row.label}`"
            @click="askRestore(props.row)"
          />
        </q-td>
      </template>
    </q-table>

    <!-- Below `sm`: the six columns become one card, with Restaurar inside thumb reach. -->
    <div v-else class="archive-cards column q-gutter-sm">
      <q-card
        v-for="entry in rows"
        :key="entry.archiveId"
        flat
        bordered
        class="archive-card"
      >
        <q-card-section class="q-py-sm">
          <div class="row items-center no-wrap">
            <q-icon
              :name="entry.icon || 'inventory_2'"
              size="18px"
              class="text-grey-7 q-mr-xs"
            />
            <span class="text-body2 text-weight-medium ellipsis">{{ entry.label }}</span>
            <span class="text-caption text-grey-7 q-ml-xs">#{{ entry.itemId }}</span>
            <q-space />
            <q-badge
              :color="entry.restoredAtUtc !== null ? 'positive' : 'grey-6'"
              rounded
              class="q-ml-sm"
            >
              {{ entry.restoredAtUtc !== null ? 'Restaurado' : 'En archivo' }}
            </q-badge>
          </div>
          <div class="text-caption text-grey-7 q-mt-xs">
            {{ formatTimestamp(entry.deletedAtUtc) }} · {{ entry.deletedBy || '—' }}
          </div>
        </q-card-section>

        <q-card-actions class="q-pt-none">
          <div class="row items-center full-width q-gutter-x-sm">
            <q-icon
              v-if="entry.attachmentsJson !== null"
              name="attach_file"
              size="18px"
              class="text-grey-8"
              aria-label="Tenía adjuntos"
            >
              <q-tooltip>Tenía adjuntos archivados</q-tooltip>
            </q-icon>
            <span v-else class="text-caption text-grey-5">Sin adjuntos</span>
            <q-space />
            <q-btn
              flat
              dense
              no-caps
              icon="restore"
              label="Restaurar"
              :loading="busyId === entry.archiveId"
              :disable="entry.restoredAtUtc !== null"
              :aria-label="`Restaurar ${entry.label}`"
              @click="askRestore(entry)"
            />
          </div>
        </q-card-actions>
      </q-card>

      <div v-if="!loading && rows.length === 0" class="text-caption text-grey-7">
        No hay registros en el archivo
      </div>
    </div>

    <div class="row items-center q-mt-sm">
      <div class="text-caption text-grey-7">
        <span v-if="total !== null">{{ total }} registro(s) · </span>página {{ page
        }}<span v-if="pageCount !== null"> de {{ pageCount }}</span>
      </div>
      <q-space />
      <q-btn
        flat
        dense
        round
        icon="chevron_left"
        aria-label="Página anterior"
        :disable="page <= 1 || loading"
        @click="goTo(page - 1)"
      />
      <q-btn
        flat
        dense
        round
        icon="chevron_right"
        aria-label="Página siguiente"
        :disable="!canGoNext || loading"
        @click="goTo(page + 1)"
      />
    </div>

    <!-- Restoring is the reverse of a deletion: ask first, and say exactly what comes back. -->
    <q-dialog v-model="showConfirm" persistent>
      <q-card class="confirm-card q-pa-md">
        <div class="text-subtitle1 q-mb-sm">Restaurar registro</div>
        <div class="text-body2 q-mb-md">
          ¿Restaurar <strong>{{ pending?.label }}</strong
          >? Volverá al inventario con sus adjuntos.
        </div>
        <div class="row justify-end q-gutter-sm">
          <q-btn flat no-caps label="Cancelar" @click="cancelRestore" />
          <q-btn
            unelevated
            no-caps
            color="primary"
            label="Restaurar"
            :loading="restoring"
            @click="confirmRestore"
          />
        </div>
      </q-card>
    </q-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { ApiError } from '@services/api';
import { archiveService, type ArchiveEntry } from '@services/archive';
import { formatTimestamp } from '@services/utilities';
import { useFeedback } from '../../composables/useFeedback';

const feedback = useFeedback();

const rows = ref<ArchiveEntry[]>([]);
// The API answers `total` on the first page; a later page may leave it out, so an unknown total is
// null rather than 0 — the pager keeps what it was told and never claims "page 3 of 1".
const total = ref<number | null>(null);
/** The server's own answer to "is there another page", which needs no total. */
const hasMore = ref<boolean | null>(null);
const page = ref(1);
const pageSize = ref(25);
const loading = ref(false);
const errorMessage = ref<string | null>(null);
const notice = ref<string | null>(null);

const busyId = ref<number | null>(null);
const showConfirm = ref(false);
const restoring = ref(false);
const pending = ref<ArchiveEntry | null>(null);

const filters = reactive({
  label: '',
  actor: '',
  from: '',
  to: '',
});

const columns = [
  { name: 'label', label: 'Etiqueta', field: 'label', align: 'left' as const, sortable: true },
  { name: 'deletedAtUtc', label: 'Eliminado', field: 'deletedAtUtc', align: 'left' as const },
  { name: 'deletedBy', label: 'Eliminado por', field: 'deletedBy', align: 'left' as const },
  { name: 'attachments', label: 'Adjuntos', field: 'attachmentsJson', align: 'left' as const },
  { name: 'state', label: 'Estado', field: 'restoredAtUtc', align: 'left' as const },
  { name: 'actions', label: '', field: 'archiveId', align: 'right' as const },
];

/** The API reports the total, so the last page is arithmetic and not "is this page full". */
const pageCount = computed(() =>
  total.value === null ? null : Math.max(1, Math.ceil(total.value / pageSize.value)),
);

/** Without a total the only honest signal is a full page: `pageCount` is unknown, not one. */
const canGoNext = computed(() => {
  // The server knows whether another page exists; a full page is only a guess at it.
  if (hasMore.value !== null) return hasMore.value;
  return pageCount.value === null ? rows.value.length >= pageSize.value : page.value < pageCount.value;
});

async function load(): Promise<void> {
  loading.value = true;
  errorMessage.value = null;
  try {
    const result = await archiveService.list({
      label: filters.label,
      actor: filters.actor,
      from: filters.from,
      to: filters.to,
      page: page.value,
      pageSize: pageSize.value,
    });
    rows.value = result.items;
    // A later page may answer without a total: keep the one the first page gave instead of zeroing it.
    if (typeof result.total === 'number' && Number.isFinite(result.total)) {
      total.value = result.total;
    }
    hasMore.value = typeof result.hasMore === 'boolean' ? result.hasMore : null;
    pageSize.value = result.pageSize;
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : String(error);
    rows.value = [];
    total.value = 0;
  } finally {
    loading.value = false;
  }
}

function apply(): void {
  page.value = 1;
  notice.value = null;
  void load();
}

function clear(): void {
  filters.label = '';
  filters.actor = '';
  filters.from = '';
  filters.to = '';
  apply();
}

function goTo(next: number): void {
  const ceiling = pageCount.value;
  page.value = ceiling === null ? Math.max(1, next) : Math.min(ceiling, Math.max(1, next));
  notice.value = null;
  void load();
}

function askRestore(entry: ArchiveEntry): void {
  pending.value = entry;
  showConfirm.value = true;
}

function cancelRestore(): void {
  showConfirm.value = false;
  pending.value = null;
}

async function confirmRestore(): Promise<void> {
  const entry = pending.value;
  if (!entry) return;

  restoring.value = true;
  busyId.value = entry.archiveId;
  errorMessage.value = null;
  notice.value = null;
  try {
    await archiveService.restore(entry.itemId);
    showConfirm.value = false;
    pending.value = null;
    feedback.success(`«${entry.label}» volvió al inventario.`);
    // The archive now holds a restored snapshot: ask the API instead of patching the row by hand.
    await load();
  } catch (error) {
    showConfirm.value = false;
    pending.value = null;
    if (error instanceof ApiError && error.status === 404) {
      notice.value = `«${entry.label}» ya estaba restaurado: no queda ninguna copia por restaurar.`;
      await load();
    } else {
      const message = error instanceof Error ? error.message : String(error);
      errorMessage.value = message;
      feedback.error(error, 'No se pudo restaurar el registro.');
    }
  } finally {
    restoring.value = false;
    busyId.value = null;
  }
}

onMounted(load);
</script>

<style scoped>
.confirm-card {
  width: 100%;
  max-width: 420px;
}
</style>
