<template>
  <div class="audit-panel">
    <div class="row items-center q-gutter-sm q-mb-md">
      <div class="text-subtitle1 text-weight-medium">Auditoría</div>
      <q-space />
      <q-btn flat dense no-caps icon="refresh" label="Recargar" :loading="loading" @click="load" />
      <q-btn
        outline
        dense
        no-caps
        icon="download"
        label="Exportar CSV"
        :disable="rows.length === 0"
        @click="exportCsv"
      />
    </div>

    <div
      class="filters q-mb-md"
      :class="$q.screen.lt.sm ? 'column q-gutter-y-sm' : 'row q-col-gutter-sm'"
    >
      <div :class="$q.screen.lt.sm ? 'full-width' : 'col-12 col-sm-6 col-md-2'">
        <q-input v-model="filters.actor" dense outlined label="Actor" clearable>
          <template v-slot:prepend><q-icon name="person" size="18px" /></template>
        </q-input>
      </div>
      <div :class="$q.screen.lt.sm ? 'full-width' : 'col-12 col-sm-6 col-md-2'">
        <q-input v-model="filters.entity" dense outlined label="Entidad" clearable>
          <template v-slot:prepend><q-icon name="category" size="18px" /></template>
        </q-input>
      </div>
      <div :class="$q.screen.lt.sm ? 'full-width' : 'col-12 col-sm-6 col-md-2'">
        <q-input v-model="filters.action" dense outlined label="Acción" clearable>
          <template v-slot:prepend><q-icon name="bolt" size="18px" /></template>
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

    <q-banner v-if="pending" dense class="bg-orange-1 text-orange-10 q-mb-md">
      <template v-slot:avatar><q-icon name="construction" /></template>
      API pendiente: <code>GET /api/audit</code> todavía no existe en la API. Los filtros, el visor
      y la exportación están listos para cuando el endpoint responda.
    </q-banner>

    <q-banner v-else-if="errorMessage" dense class="bg-red-1 text-red-9 q-mb-md">
      <template v-slot:avatar><q-icon name="error_outline" /></template>
      {{ errorMessage }}
    </q-banner>

    <q-table
      v-if="!$q.screen.lt.sm"
      :rows="rows"
      :columns="columns"
      row-key="id"
      dense
      flat
      bordered
      :loading="loading"
      :rows-per-page-options="[0]"
      no-data-label="No hay cambios que coincidan"
    >
      <template v-slot:body-cell-occurredAtUtc="props">
        <q-td :props="props">{{ formatTimestamp(props.row.occurredAtUtc) }}</q-td>
      </template>

      <template v-slot:body-cell-entity="props">
        <q-td :props="props">
          {{ props.row.entity }}
          <span v-if="props.row.entityId !== null && props.row.entityId !== ''" class="text-grey-7">
            #{{ props.row.entityId }}
          </span>
        </q-td>
      </template>

      <template v-slot:body-cell-detail="props">
        <q-td :props="props" class="text-right">
          <q-btn
            flat
            dense
            no-caps
            icon="visibility"
            label="Ver"
            @click="openEntry(props.row)"
          />
        </q-td>
      </template>
    </q-table>

    <!-- Below `sm`: action and entity on the first line, who and when on the second; the whole card
         opens the before/after viewer, so the tap target is the row itself and not a small button. -->
    <div v-else class="audit-cards column q-gutter-sm">
      <q-card
        v-for="entry in rows"
        :key="entry.id"
        flat
        bordered
        clickable
        class="audit-card"
        @click="openEntry(entry)"
      >
        <q-card-section class="q-py-sm">
          <div class="row items-center no-wrap">
            <div class="col">
              <div class="text-body2 text-weight-medium ellipsis">
                {{ entry.action }} · {{ entry.entity }}
                <span
                  v-if="entry.entityId !== null && entry.entityId !== ''"
                  class="text-grey-7"
                >
                  #{{ entry.entityId }}
                </span>
              </div>
              <div class="text-caption text-grey-7 ellipsis">
                {{ entry.actorEmail }} · {{ formatTimestamp(entry.occurredAtUtc) }}
              </div>
            </div>
            <q-icon name="visibility" size="20px" class="text-primary q-ml-sm" />
          </div>
        </q-card-section>
      </q-card>

      <div v-if="!loading && rows.length === 0" class="text-caption text-grey-7">
        No hay cambios que coincidan
      </div>
    </div>

    <div class="row items-center q-mt-sm">
      <div class="text-caption text-grey-7">
        <span v-if="total !== null">{{ total }} registro(s) · </span>página {{ page }}
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
        :disable="hasMore === false || (hasMore === null && rows.length < pageSize) || loading"
        @click="goTo(page + 1)"
      />
    </div>

    <!-- Before / after of one change: the audit trail is only useful if it can be read. -->
    <q-dialog v-model="showDetail">
      <q-card class="detail-card">
        <q-card-section class="row items-center">
          <div class="column">
            <div class="text-subtitle1">{{ selected?.action }} · {{ selected?.entity }}</div>
            <div class="text-caption text-grey-7">
              {{ selected?.actorEmail }} · {{ formatTimestamp(selected?.occurredAtUtc) }}
            </div>
          </div>
          <q-space />
          <q-btn icon="close" flat round dense aria-label="Cerrar" @click="showDetail = false" />
        </q-card-section>

        <q-separator />

        <q-card-section class="detail-body">
          <div class="text-caption text-weight-medium q-mb-xs">Antes</div>
          <pre class="detail-json">{{ formatAuditPayload(selected?.before) }}</pre>

          <div class="text-caption text-weight-medium q-mb-xs">Después</div>
          <pre class="detail-json">{{ formatAuditPayload(selected?.after) }}</pre>
        </q-card-section>
      </q-card>
    </q-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { isApiPending } from '@services/api';
import { auditService, auditToCsv, formatAuditPayload, type AuditEntry } from '@services/audit';
import { formatTimestamp } from '@services/utilities';

const rows = ref<AuditEntry[]>([]);
// The API answers `total` on the first page; a later page may leave it out, and that must not turn
// the pager into "page 2 of 1". Unknown is kept as null and the footer simply omits the count.
const total = ref<number | null>(null);
/** The server's own answer to "is there another page", which needs no total. */
const hasMore = ref<boolean | null>(null);
const page = ref(1);
const pageSize = ref(25);
const loading = ref(false);
const pending = ref(false);
const errorMessage = ref<string | null>(null);

const showDetail = ref(false);
const selected = ref<AuditEntry | null>(null);

const filters = reactive({
  actor: '',
  entity: '',
  action: '',
  from: '',
  to: '',
});

const columns = [
  { name: 'occurredAtUtc', label: 'Fecha', field: 'occurredAtUtc', align: 'left' as const },
  { name: 'actorEmail', label: 'Actor', field: 'actorEmail', align: 'left' as const },
  { name: 'entity', label: 'Entidad', field: 'entity', align: 'left' as const },
  { name: 'action', label: 'Acción', field: 'action', align: 'left' as const },
  { name: 'detail', label: '', field: 'id', align: 'right' as const },
];

async function load(): Promise<void> {
  loading.value = true;
  errorMessage.value = null;
  pending.value = false;
  try {
    const result = await auditService.search({
      actor: filters.actor,
      entity: filters.entity,
      action: filters.action,
      from: filters.from,
      to: filters.to,
      page: page.value,
      pageSize: pageSize.value,
    });
    rows.value = result.items;
    if (typeof result.total === 'number' && Number.isFinite(result.total)) {
      total.value = result.total;
    }
    hasMore.value = typeof result.hasMore === 'boolean' ? result.hasMore : null;
    pageSize.value = result.pageSize;
  } catch (error) {
    if (isApiPending(error)) {
      pending.value = true;
      rows.value = [];
      total.value = 0;
    } else {
      errorMessage.value = error instanceof Error ? error.message : String(error);
    }
  } finally {
    loading.value = false;
  }
}

function apply(): void {
  page.value = 1;
  void load();
}

function clear(): void {
  filters.actor = '';
  filters.entity = '';
  filters.action = '';
  filters.from = '';
  filters.to = '';
  apply();
}

function goTo(next: number): void {
  page.value = Math.max(1, next);
  void load();
}

function openEntry(entry: AuditEntry): void {
  selected.value = entry;
  showDetail.value = true;
}

function exportCsv(): void {
  const csv = auditToCsv(rows.value);
  // A Blob and an object URL: no server round-trip, and the file is exactly what the table shows.
  const blob = new Blob([`\uFEFF${csv}`], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `auditoria-pagina-${page.value}.csv`;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

onMounted(load);
</script>

<style scoped>
.detail-card {
  width: 100%;
  max-width: 720px;
}

.detail-body {
  max-height: 70vh;
  overflow-y: auto;
}

.detail-json {
  margin: 0 0 16px;
  padding: 10px;
  border-radius: 6px;
  background: var(--imc-surface-sunken);
  font-size: 12px;
  line-height: 16px;
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
