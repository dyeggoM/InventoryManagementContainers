<template>
  <q-page class="imc-page">
    <q-linear-progress v-if="loading" indeterminate color="primary" />

    <!-- Desktop: tree on the left, form on the right. -->
    <q-splitter v-if="$q.screen.gt.sm" v-model="splitter">
      <template v-slot:before>
        <div class="q-pa-md tree-pane">
          <div class="row items-center no-wrap q-gutter-sm q-mb-md">
            <q-input
              v-if="tree.length > 0"
              v-model="query"
              dense
              outlined
              clearable
              placeholder="Search"
              class="col"
            >
              <template v-slot:prepend>
                <q-icon name="search" />
              </template>
            </q-input>
            <q-btn
              outline
              no-caps
              icon="add"
              :label="`New ${entityLabel.toLowerCase()}`"
              color="primary"
              @click="emit('create')"
            />
          </div>

          <div v-if="!loading && tree.length === 0" class="imc-empty">
            <q-icon name="inventory_2" />
            <div class="text-subtitle1 q-mt-sm">{{ emptyMessage }}</div>
          </div>
          <template v-else>
            <div v-if="!visibleTree.length" class="text-grey-7 q-pa-sm">
              Nothing matches “{{ query }}”.
            </div>
            <template v-else>
              <div v-if="query" class="text-caption text-grey-7 q-mb-sm">
                {{ matchCount }} {{ matchCount === 1 ? 'match' : 'matches' }}
              </div>

              <q-tree
                :key="treeVersion"
            :nodes="visibleTree"
            node-key="id"
            no-connectors
            icon="chevron_right"
            selected-color="primary"
            :selected="selected"
            v-model:expanded="expanded"
            class="imc-tree"
            @update:selected="onSelect"
          >
            <template v-slot:default-header="prop">
              <div class="row items-center no-wrap imc-tree__row">
                <q-icon v-if="prop.node.icon" :name="prop.node.icon" size="18px" class="q-mr-xs" />
                <div class="column imc-tree__text">
                  <span
                    class="ellipsis imc-tree__label"
                    :class="{ 'text-weight-medium': prop.node.children?.length }"
                    >{{ prop.node.label }}</span
                  >
                  <span v-if="prop.node.match" class="ellipsis imc-tree__match">
                    {{ prettyLabel(prop.node.match.label) }} · {{ short(prop.node.match.value) }}
                  </span>
                </div>
                <q-icon
                  v-if="prop.node.attachmentCount"
                  name="attach_file"
                  size="16px"
                  class="imc-attach"
                  aria-label="Has attachments"
                />
                <q-space />
                <span
                  v-if="prop.node.children?.length && !expanded.includes(prop.node.id)"
                  class="imc-count"
                >
                  {{ prop.node.children.length }}
                </span>
                <q-icon v-else name="chevron_right" size="18px" class="imc-chevron" />
              </div>
                </template>
              </q-tree>
            </template>
          </template>
        </div>
      </template>

      <template v-slot:after>
        <div v-if="selected < 0" class="imc-empty">
          <q-icon name="touch_app" />
          <div class="text-subtitle1 q-mt-sm">Select an item to edit it</div>
          <div class="text-caption q-mt-xs">or start a new one</div>
        </div>
        <q-tab-panels
          v-model="selected"
          animated
          transition-prev="jump-up"
          transition-next="jump-up"
        >
          <q-tab-panel v-for="panel in panels" :key="panel.id" :name="panel.id">
            <div class="row items-center justify-between q-mb-md">
              <div class="column">
                <div class="text-h6">
                  {{ panel.id > 0 ? entryLabel || `${entityLabel} #${panel.id}` : `New ${entityLabel.toLowerCase()}` }}
                </div>
                <div v-if="panel.id > 0" class="text-caption">
                  <span class="text-grey-7">{{ entityLabel }} #{{ panel.id }}</span>
                  <span v-if="dirty" class="text-orange-8 q-ml-sm">· unsaved changes</span>
                </div>
              </div>
              <q-btn v-if="panel.id > 0" flat round dense icon="more_vert" aria-label="More actions">
                <q-menu anchor="bottom right" self="top right" auto-close>
                  <q-list dense style="min-width: 200px">
                    <q-item clickable v-close-popup @click="emit('duplicate')">
                      <q-item-section avatar>
                        <q-icon name="content_copy" />
                      </q-item-section>
                      <q-item-section>Duplicate {{ entityLabel.toLowerCase() }}</q-item-section>
                    </q-item>
                    <q-separator />
                    <q-item clickable v-close-popup @click="emit('delete')">
                      <q-item-section avatar>
                        <q-icon name="delete_outline" color="negative" />
                      </q-item-section>
                      <q-item-section>Delete {{ entityLabel.toLowerCase() }}</q-item-section>
                    </q-item>
                  </q-list>
                </q-menu>
              </q-btn>
            </div>
            <q-form @submit="emit('submit')" @reset="emit('reset')" class="q-gutter-md">
              <slot />
              <div class="row q-gutter-sm q-mt-md">
                <q-btn label="Save" type="submit" color="primary" :loading="saving" />
                <q-btn label="Reset" type="reset" color="grey-9" flat />
              </div>
            </q-form>
          </q-tab-panel>
        </q-tab-panels>
      </template>
    </q-splitter>

    <!-- Phones: the list is the page, the form opens as a full-height sheet. -->
    <div v-else class="q-pa-md tree-pane">
      <div v-if="tree.length > 0" class="q-mb-md">
        <q-input v-model="query" dense outlined clearable placeholder="Search">
          <template v-slot:prepend>
            <q-icon name="search" />
          </template>
        </q-input>
      </div>

      <div v-if="!loading && tree.length === 0" class="imc-empty">
        <q-icon name="inventory_2" />
        <div class="text-subtitle1 q-mt-sm q-mb-md">{{ emptyMessage }}</div>
        <q-btn
          unelevated
          no-caps
          color="primary"
          label="Create the first one"
          icon="add"
          @click="emit('create')"
        />
      </div>
      <template v-else>
        <div v-if="!visibleTree.length" class="text-grey-7">
          Nothing matches “{{ query }}”.
        </div>
        <template v-else>
          <div v-if="query" class="text-caption text-grey-7 q-mb-sm">
            {{ matchCount }} {{ matchCount === 1 ? 'match' : 'matches' }}
          </div>

          <q-tree
            :key="treeVersion"
        :nodes="visibleTree"
        node-key="id"
        no-connectors
        icon="chevron_right"
        selected-color="primary"
        :selected="selected"
        v-model:expanded="expanded"
        class="imc-tree"
        @update:selected="onSelect"
      >
        <template v-slot:default-header="prop">
          <div class="row items-center no-wrap imc-tree__row">
            <div class="column imc-tree__text">
              <span
                class="ellipsis imc-tree__label"
                :class="{ 'text-weight-medium': prop.node.children?.length }"
                >{{ prop.node.label }}</span
              >
              <span v-if="prop.node.match" class="ellipsis imc-tree__match">
                {{ prettyLabel(prop.node.match.label) }} · {{ short(prop.node.match.value) }}
              </span>
            </div>
            <q-icon
              v-if="prop.node.attachmentCount"
              name="attach_file"
              size="16px"
              class="imc-attach"
              aria-label="Has attachments"
            />
            <q-space />
            <span
              v-if="prop.node.children?.length && !expanded.includes(prop.node.id)"
              class="imc-count"
            >
              {{ prop.node.children.length }}
            </span>
            <q-icon v-else name="chevron_right" size="18px" class="imc-chevron" />
          </div>
            </template>
          </q-tree>
        </template>
      </template>

      <q-page-sticky position="bottom-right" :offset="[16, 16]">
        <q-btn
          round
          unelevated
          no-caps
          icon="add"
          :label="`New ${entityLabel.toLowerCase()}`"
          color="primary"
          class="imc-fab"
          aria-label="New item"
          @click="emit('create')"
        />
      </q-page-sticky>
    </div>

    <q-dialog
      v-model="dialog"
      :maximized="$q.screen.lt.md"
      :persistent="dirty"
      transition-show="slide-up"
      transition-hide="slide-down"
    >
      <q-card class="dialog-card column no-wrap">
        <q-toolbar class="dialog-header q-px-md">
          <q-toolbar-title class="text-subtitle1 text-weight-medium ellipsis">
            {{ entryId > 0 ? entryLabel || `${entityLabel} #${entryId}` : `New ${entityLabel.toLowerCase()}` }}
          </q-toolbar-title>
          <q-btn
            v-if="entryId > 0"
            icon="more_vert"
            flat
            round
            dense
            aria-label="More actions"
            class="q-mr-xs"
          >
            <q-menu anchor="bottom right" self="top right" auto-close>
              <q-list dense style="min-width: 200px">
                <q-item clickable v-close-popup @click="emit('duplicate')">
                  <q-item-section avatar>
                    <q-icon name="content_copy" />
                  </q-item-section>
                  <q-item-section>Duplicate {{ entityLabel.toLowerCase() }}</q-item-section>
                </q-item>
                <q-separator />
                <q-item clickable v-close-popup @click="emit('delete')">
                  <q-item-section avatar>
                    <q-icon name="delete_outline" color="negative" />
                  </q-item-section>
                  <q-item-section>Delete {{ entityLabel.toLowerCase() }}</q-item-section>
                </q-item>
              </q-list>
            </q-menu>
          </q-btn>
          <q-btn icon="close" flat round dense aria-label="Close" @click="requestClose" />
        </q-toolbar>

        <q-form @submit="emit('submit')" @reset="emit('reset')" class="col column no-wrap">
          <q-card-section class="col q-pt-md dialog-body">
            <slot />
          </q-card-section>

          <div class="dialog-actions row items-center q-px-md q-py-sm">
            <span v-if="dirty" class="text-caption imc-unsaved">Unsaved changes</span>
            <q-space />
            <div class="row items-center q-gutter-x-sm">
              <q-btn label="Reset" color="grey-9" flat type="reset" />
              <q-btn label="Save" color="primary" type="submit" :loading="saving" class="q-px-lg" />
            </div>
          </div>
        </q-form>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import type { TreeEntry } from '../composables/useTreeEditor';
import { useFeedback } from '../composables/useFeedback';
import { prettyLabel } from '../services/utilities';

const feedback = useFeedback();

/**
 * The master-detail shell shared by the inventory and the configuration pages: a tree on the left,
 * a form per node on the right, the same form in a full-height sheet on phones. Only the fields
 * inside the form belong to the page (the default slot).
 */
const props = withDefaults(
  defineProps<{
    tree: TreeEntry[];
    panels: TreeEntry[];
    /** Id of the node currently in the form; drives the sheet title and the delete affordance. */
    entryId: number;
    treeVersion: number;
    loading: boolean;
    saving: boolean;
    emptyMessage: string;
    entityLabel?: string;
    /** The record's own name, shown as the panel title when there is one. */
    entryLabel?: string;
    /** The open form has edits that are not saved yet. */
    dirty?: boolean;
  }>(),
  { entityLabel: 'Item', entryLabel: '', dirty: false },
);

const emit = defineEmits<{
  select: [id: number];
  create: [];
  submit: [];
  reset: [];
  delete: [];
  duplicate: [];
  close: [];
}>();

const selected = defineModel<number>('selected', { required: true });
const expanded = defineModel<number[]>('expanded', { required: true });
const splitter = defineModel<number>('splitter', { default: 40 });
const dialog = defineModel<boolean>('dialog', { default: false });

const query = ref('');

/** A long value would push the row wide; the label is what matters, the value is a hint. */
function short(value: string): string {
  const text = value.replace(/\s+/g, ' ').trim();
  return text.length > 34 ? `${text.slice(0, 33)}…` : text;
}

function collectIds(nodes: TreeEntry[], acc: number[] = []): number[] {
  for (const node of nodes) {
    if ((node.children ?? []).length > 0) {
      acc.push(node.id);
      collectIds(node.children ?? [], acc);
    }
  }
  return acc;
}

/** The first attribute whose key or value contains the needle; the list shows that pair. */
function attributeMatch(node: TreeEntry, needle: string): { label: string; value: string } | null {
  if (!node.attributes) return null;
  try {
    const parsed: unknown = JSON.parse(node.attributes);
    if (!parsed || typeof parsed !== 'object') return null;
    // Keys are matched with punctuation removed, so "buy price" finds BuyPrice and "serial" finds
    // a key somebody typed by hand; values match as plain substrings.
    const needleKey = needle.replace(/[^a-z0-9]/g, '');
    for (const [key, raw] of Object.entries(parsed as Record<string, unknown>)) {
      // Only scalars are searchable: an object would stringify to "[object Object]".
      const value =
        typeof raw === 'string'
          ? raw
          : typeof raw === 'number' || typeof raw === 'boolean'
            ? String(raw)
            : '';
      const keyHit =
        needleKey.length > 0 && key.toLowerCase().replace(/[^a-z0-9]/g, '').includes(needleKey);
      if (keyHit || value.toLowerCase().includes(needle)) {
        return { label: key, value };
      }
    }
  } catch {
    // A malformed row simply does not match by attribute; the label search still works.
  }
  return null;
}

function filterTree(nodes: TreeEntry[], needle: string): TreeEntry[] {
  const result: TreeEntry[] = [];
  for (const node of nodes) {
    const children = filterTree(node.children ?? [], needle);
    const byLabel = node.label.toLowerCase().includes(needle);
    const match = byLabel ? null : attributeMatch(node, needle);
    if (byLabel || children.length > 0 || match) {
      // hit: this row matched by itself; a parent kept only for context does not count.
      result.push({ ...node, children, match, hit: byLabel || match !== null });
    }
  }
  return result;
}

const visibleTree = computed(() => {
  const needle = query.value.trim().toLowerCase();
  return needle ? filterTree(props.tree, needle) : props.tree;
});

/** Only rows that matched themselves: a parent shown for context is not a result. */
function countMatches(nodes: TreeEntry[]): number {
  return nodes.reduce(
    (total, node) => total + (node.hit ? 1 : 0) + countMatches(node.children ?? []),
    0,
  );
}

const matchCount = computed(() => countMatches(visibleTree.value));

// Matches have to be visible to be useful, so a search expands whatever it finds.
watch(visibleTree, (nodes) => {
  if (query.value.trim()) expanded.value = collectIds(nodes);
});

// q-tree emits null when the selection is cleared (clicking the selected node again). Turning
// that into a null model hides every tab panel, so the last selection is kept instead.
async function onSelect(id: number | null) {
  if (typeof id !== 'number') return;
  if (id !== props.entryId && props.dirty) {
    const discard = await feedback.confirmDiscard('This record has edits that are not saved.');
    if (!discard) {
      selected.value = props.entryId; // put the highlight back on the record that still holds them
      return;
    }
    emit('reset'); // the page clears the fields, so the new selection cannot inherit them
  }
  selected.value = id;
  emit('select', id);
}

/** The sheet is the only exit on a phone, so it asks before dropping edits. */
async function requestClose() {
  if (props.dirty) {
    const discard = await feedback.confirmDiscard('This record has edits that are not saved.');
    if (!discard) return;
    emit('reset');
  }
  dialog.value = false;
  emit('close');
}
</script>

<style scoped>
.tree-pane :deep(.q-tree__node-header) {
  min-height: 44px; /* comfortable tap target */
  align-items: center;
}

.imc-tree__row {
  flex: 1 1 auto;
  min-width: 0;
}

.imc-tree__text {
  min-width: 0;
}

.imc-tree__label {
  font-size: 15px;
}

.imc-tree__match {
  font-size: 12px;
  line-height: 16px;
  color: #6b7280;
}

.imc-count {
  min-width: 22px;
  padding: 0 6px;
  border-radius: 11px;
  background: #eef1f5;
  color: #5b6472;
  font-size: 12px;
  line-height: 20px;
  text-align: center;
}

.imc-chevron {
  color: #9aa1ac;
}

.imc-attach {
  color: #8a93a0;
  margin-left: 6px;
}

.imc-unsaved {
  color: #b26a00;
  font-weight: 500;
}

/* Sibling rows get a hairline; a parent row is tinted so the groups read as blocks. */
.imc-tree :deep(.q-tree__node + .q-tree__node) {
  border-top: 1px solid #eff1f4;
}

.imc-tree :deep(.q-tree__node--parent > .q-tree__node-header) {
  background: #fafbfc;
}

.imc-fab {
  padding-left: 18px;
  padding-right: 22px;
}

.tree-pane :deep(.q-tree__arrow) {
  padding: 8px 4px; /* the chevron is a target too */
  color: #9aa1ac;
}

.dialog-card {
  width: 100%;
  max-height: 100vh;
}

@media (min-width: 1024px) {
  .dialog-card {
    min-width: 460px;
    max-height: 85vh;
  }
}

.dialog-header {
  border-bottom: 1px solid rgba(0, 0, 0, 0.08);
  padding-top: calc(4px + env(safe-area-inset-top));
}

.dialog-body {
  overflow-y: auto;
  padding-bottom: 32px; /* the last row must clear the fixed footer */
}

/* Save stays reachable on a phone, whatever the form length. */
.dialog-actions {
  border-top: 1px solid rgba(0, 0, 0, 0.08);
  background: #fff;
  padding-bottom: calc(10px + env(safe-area-inset-bottom));
}
</style>