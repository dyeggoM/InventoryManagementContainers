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
          <div v-else-if="!visibleTree.length" class="text-grey-7 q-pa-sm">
            Nothing matches “{{ query }}”.
          </div>
          <div v-else-if="query" class="text-caption text-grey-7 q-mb-sm">
            {{ matchCount }} {{ matchCount === 1 ? 'match' : 'matches' }}
          </div>

          <q-tree
            v-else
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
                <span
                  class="ellipsis imc-tree__label"
                  :class="{ 'text-weight-medium': prop.node.children?.length }"
                  >{{ prop.node.label }}</span
                >
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
                <div v-if="panel.id > 0" class="text-caption text-grey-7">
                  {{ entityLabel }} #{{ panel.id }}
                </div>
              </div>
              <q-btn v-if="panel.id > 0" flat round dense icon="more_vert" aria-label="More actions">
                <q-menu anchor="bottom right" self="top right" auto-close>
                  <q-list dense style="min-width: 180px">
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
      <div v-else-if="!visibleTree.length" class="text-grey-7">
        Nothing matches “{{ query }}”.
      </div>
      <div v-else-if="query" class="text-caption text-grey-7 q-mb-sm">
        {{ matchCount }} {{ matchCount === 1 ? 'match' : 'matches' }}
      </div>

      <q-tree
        v-else
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
            <span
              class="ellipsis imc-tree__label"
              :class="{ 'text-weight-medium': prop.node.children?.length }"
              >{{ prop.node.label }}</span
            >
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
              <q-list dense style="min-width: 180px">
                <q-item clickable v-close-popup @click="emit('delete')">
                  <q-item-section avatar>
                    <q-icon name="delete_outline" color="negative" />
                  </q-item-section>
                  <q-item-section>Delete {{ entityLabel.toLowerCase() }}</q-item-section>
                </q-item>
              </q-list>
            </q-menu>
          </q-btn>
          <q-btn icon="close" flat round dense v-close-popup aria-label="Close" />
        </q-toolbar>

        <q-form @submit="emit('submit')" @reset="emit('reset')" class="col column no-wrap">
          <q-card-section class="col q-pt-md dialog-body">
            <slot />
          </q-card-section>

          <div class="dialog-actions row items-center justify-end q-gutter-sm q-px-md q-py-sm">
            <q-btn label="Reset" color="grey-9" flat type="reset" />
            <q-btn label="Save" color="primary" type="submit" :loading="saving" class="q-px-lg" />
          </div>
        </q-form>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import type { TreeEntry } from '../composables/useTreeEditor';

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
  }>(),
  { entityLabel: 'Item', entryLabel: '' },
);

const emit = defineEmits<{
  select: [id: number];
  create: [];
  submit: [];
  reset: [];
  delete: [];
}>();

const selected = defineModel<number>('selected', { required: true });
const expanded = defineModel<number[]>('expanded', { required: true });
const splitter = defineModel<number>('splitter', { default: 40 });
const dialog = defineModel<boolean>('dialog', { default: false });

const query = ref('');

function collectIds(nodes: TreeEntry[], acc: number[] = []): number[] {
  for (const node of nodes) {
    if ((node.children ?? []).length > 0) {
      acc.push(node.id);
      collectIds(node.children ?? [], acc);
    }
  }
  return acc;
}

function filterTree(nodes: TreeEntry[], needle: string): TreeEntry[] {
  const result: TreeEntry[] = [];
  for (const node of nodes) {
    const children = filterTree(node.children ?? [], needle);
    if (node.label.toLowerCase().includes(needle) || children.length > 0) {
      result.push({ ...node, children });
    }
  }
  return result;
}

const visibleTree = computed(() => {
  const needle = query.value.trim().toLowerCase();
  return needle ? filterTree(props.tree, needle) : props.tree;
});

function countMatches(nodes: TreeEntry[]): number {
  return nodes.reduce((total, node) => total + 1 + countMatches(node.children ?? []), 0);
}

const matchCount = computed(() => countMatches(visibleTree.value));

// Matches have to be visible to be useful, so a search expands whatever it finds.
watch(visibleTree, (nodes) => {
  if (query.value.trim()) expanded.value = collectIds(nodes);
});

// q-tree emits null when the selection is cleared (clicking the selected node again). Turning
// that into a null model hides every tab panel, so the last selection is kept instead.
function onSelect(id: number | null) {
  if (typeof id !== 'number') return;
  selected.value = id;
  emit('select', id);
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

.imc-tree__label {
  font-size: 15px;
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