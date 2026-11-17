<template>
  <div class="imc-section set-pieces">
    <div class="row items-center justify-between q-mb-xs">
      <div class="imc-section__title q-mb-none">Pieces</div>
      <q-btn
        v-if="!readonly"
        flat
        no-caps
        dense
        icon="playlist_add"
        label="Add pieces"
        color="primary"
        type="button"
        :disable="busy"
        @click="openPicker"
      />
    </div>

    <div v-if="pieces.length === 0" class="text-caption text-grey-7">
      No pieces yet — this set is an item like any other, and its pieces are the items below it.
    </div>

    <div v-else class="set-pieces__list">
      <div v-for="piece in pieces" :key="piece.id" class="set-pieces__row">
        <q-icon :name="piece.icon || 'inventory_2'" size="20px" class="set-pieces__icon" />
        <div class="set-pieces__body">
          <div class="text-body2 ellipsis">{{ piece.label }}</div>
          <div v-if="detail(piece)" class="text-caption text-grey-7 ellipsis">
            {{ detail(piece) }}
          </div>
        </div>
        <q-btn
          v-if="!readonly"
          flat
          round
          dense
          icon="remove_circle_outline"
          color="grey-6"
          type="button"
          :aria-label="`Remove ${piece.label} from the set`"
          :disable="busy"
          @click="emit('remove', piece.id)"
        />
      </div>
    </div>

    <!--
      The picker lists the items that have no parent: an item already in a set is not offered,
      because attaching it would move it out of the set that owns it. Both the search and the
      match line are the tree's own (`services/search.ts`), so the dialog reads like the list.
    -->
    <q-dialog
      v-model="picker"
      :maximized="$q.screen.lt.sm"
      transition-show="slide-up"
      transition-hide="slide-down"
    >
      <q-card class="picker-card column no-wrap">
        <q-toolbar class="picker-header q-px-md">
          <q-toolbar-title class="text-subtitle1 text-weight-medium">Add pieces</q-toolbar-title>
          <q-btn icon="close" flat round dense aria-label="Close" @click="picker = false" />
        </q-toolbar>

        <q-card-section class="q-pt-md q-pb-sm">
          <q-input v-model="query" dense outlined clearable placeholder="Search" autofocus>
            <template v-slot:prepend>
              <q-icon name="search" />
            </template>
          </q-input>
          <div v-if="query" class="text-caption text-grey-7 q-mt-xs">
            {{ matchCount }} {{ matchCount === 1 ? 'match' : 'matches' }}
          </div>
        </q-card-section>

        <q-card-section class="col picker-list">
          <div v-if="candidates.length === 0" class="text-grey-7 q-pa-sm">
            Nothing left to add: every item already belongs to a set. Remove one from its set first.
          </div>
          <div v-else-if="visible.length === 0" class="text-grey-7 q-pa-sm">
            Nothing matches “{{ query }}”.
          </div>
          <q-item v-for="node in visible" :key="node.id" tag="label" clickable class="picker-row">
            <q-item-section side top>
              <q-checkbox v-model="chosen" :val="node.id" color="primary" />
            </q-item-section>
            <q-item-section>
              <q-item-label class="ellipsis">{{ node.label }}</q-item-label>
              <q-item-label v-if="node.match" caption class="ellipsis">
                {{ prettyLabel(node.match.label) }} · {{ shortValue(node.match.value) }}
              </q-item-label>
              <q-item-label v-else-if="detail(node)" caption class="ellipsis">
                {{ detail(node) }}
              </q-item-label>
            </q-item-section>
          </q-item>
        </q-card-section>

        <div class="picker-actions row items-center q-px-md q-py-sm">
          <span class="text-caption text-grey-7">{{ chosen.length }} selected</span>
          <q-space />
          <q-btn flat no-caps label="Cancel" @click="picker = false" />
          <q-btn
            unelevated
            no-caps
            color="primary"
            class="q-ml-sm"
            :label="confirmLabel"
            :disable="chosen.length === 0 || busy"
            @click="confirm"
          />
        </div>
      </q-card>
    </q-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import type { Item } from '@services/items';
import type { TreeEntry } from '../composables/useTreeEditor';
import { countMatches, filterTree, shortValue } from '../services/search';
import { prettyLabel } from '../services/utilities';

/**
 * The pieces of a set: its children, listed where the set itself is edited.
 *
 * The inventory has always shown a parent's count on the tree row; what it could not do is decide
 * which items belong to a set from the set. Attaching and detaching are the page's calls (it owns
 * the API and the reload), so this component only reports what the user picked.
 *
 * `candidates` is the organization's parentless items, minus this set: an item that already has a
 * parent is not a candidate, because attaching it would silently move it out of its own set.
 */
const props = withDefaults(
  defineProps<{
    /** The items currently under this set, in the order the tree returned them. */
    pieces: Item[];
    /** The items this set may take (see above). */
    candidates: Item[];
    /** The session cannot write: no adding and no removing. */
    readonly?: boolean;
    /** A request is in flight: the two writes wait for it instead of racing. */
    busy?: boolean;
  }>(),
  { readonly: false, busy: false },
);

const emit = defineEmits<{
  add: [ids: number[]];
  remove: [id: number];
}>();

const picker = ref(false);
const query = ref('');
const chosen = ref<number[]>([]);

/** The candidates as search nodes: no children, or a match would pull unrelated rows in. */
const searchable = computed<TreeEntry[]>(() =>
  props.candidates.map((item) => ({
    id: item.id,
    label: item.label,
    icon: item.icon,
    attributes: item.attributes,
    children: [],
  })),
);

const visible = computed<TreeEntry[]>(() => {
  const needle = query.value.trim().toLowerCase();
  return needle ? filterTree(searchable.value, needle) : searchable.value;
});

const matchCount = computed(() => countMatches(visible.value));

const confirmLabel = computed(() =>
  chosen.value.length === 1 ? 'Add 1 piece' : `Add ${chosen.value.length} pieces`,
);

function openPicker() {
  query.value = '';
  chosen.value = [];
  picker.value = true;
}

/** One confirm for the whole selection: the page posts it as one request. */
function confirm() {
  if (chosen.value.length === 0) return;
  emit('add', [...chosen.value]);
  picker.value = false;
  chosen.value = [];
}

/** What the row says besides its name: the brand and the code the inventory already carries. */
function detail(entry: { attributes?: string | null | undefined }): string {
  const attributes = attributesOf(entry.attributes);
  return [attributes.Brand, attributes.Code]
    .map((value) => (value ?? '').trim())
    .filter(Boolean)
    .join(' · ');
}

/** A record whose attributes are not a JSON object simply has none to show. */
function attributesOf(raw: string | null | undefined): Record<string, string> {
  if (!raw) return {};
  try {
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, string>) : {};
  } catch {
    return {};
  }
}
</script>

<style scoped>
.set-pieces__list {
  display: grid;
  row-gap: 2px;
}

/* A row is a name, its facts and one action: the same list, not a gallery, that the files use. */
.set-pieces__row {
  display: grid;
  grid-template-columns: 24px minmax(0, 1fr) 36px;
  column-gap: 10px;
  align-items: center;
  min-height: 48px;
  padding: 6px 0;
  border-bottom: 1px solid var(--imc-hairline);
}

.set-pieces__row:last-child {
  border-bottom: 0;
}

.set-pieces__icon {
  color: var(--imc-text-soft);
}

.set-pieces__body {
  min-width: 0;
}

.picker-card {
  width: 100%;
  max-height: 100vh;
}

@media (min-width: 1024px) {
  .picker-card {
    min-width: 420px;
    max-height: 80vh;
  }
}

.picker-header {
  border-bottom: 1px solid var(--imc-hairline);
  padding-top: calc(4px + env(safe-area-inset-top));
}

.picker-list {
  overflow-y: auto;
  overscroll-behavior: contain;
}

.picker-row {
  border-bottom: 1px solid var(--imc-hairline);
  padding-top: 8px;
  padding-bottom: 8px;
}

/* One confirm stays reachable on a phone, whatever the length of the list. */
.picker-actions {
  border-top: 1px solid var(--imc-hairline);
  background: var(--imc-surface);
  padding-bottom: calc(10px + env(safe-area-inset-bottom));
}
</style>
