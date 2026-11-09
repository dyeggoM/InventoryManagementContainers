<template>
  <q-page>
    <q-linear-progress v-if="loading" indeterminate color="primary" />

    <q-splitter v-model="splitter" v-if="$q.screen.gt.sm">
      <template v-slot:before>
        <div class="q-pa-md tree-pane">
          <q-input
            v-if="tree.length > 1"
            v-model="query"
            dense
            outlined
            clearable
            placeholder="Search"
            class="q-mb-md"
          >
            <template v-slot:prepend>
              <q-icon name="search" />
            </template>
          </q-input>

          <div v-if="!loading && tree.length <= 1" class="text-grey-7 q-mb-md">
            {{ emptyMessage }}
          </div>
          <div v-else-if="!visibleTree.length" class="text-grey-7">Nothing matches “{{ query }}”.</div>

          <q-tree
            :key="treeVersion"
            :nodes="visibleTree"
            node-key="id"
            selected-color="primary"
            :selected="selected"
            v-model:expanded="expanded"
            @update:selected="onSelect"
          >
            <template v-slot:default-header="prop">
              <div class="row items-center">
                <q-icon v-if="prop.node.icon" :name="prop.node.icon" />
                <div>
                  <span>{{ prop.node.id }} - {{ prop.node.label }}</span>
                </div>
              </div>
            </template>
          </q-tree>
        </div>
      </template>

      <template v-slot:after>
        <q-tab-panels
          v-model="selected"
          animated
          transition-prev="jump-up"
          transition-next="jump-up"
        >
          <q-tab-panel v-for="panel in panels" :key="panel.id" :name="panel.id">
            <div class="row items-center justify-between q-mb-md">
              <div class="text-h4">Id: {{ panel.id }}</div>
              <q-btn
                v-if="panel.id > 0"
                label="Delete"
                type="button"
                color="negative"
                flat
                @click="emit('delete')"
              />
            </div>
            <q-form @submit="emit('submit')" @reset="emit('reset')" class="q-gutter-md">
              <slot />
              <div class="row q-gutter-sm">
                <q-btn label="Submit" type="submit" color="primary" :loading="saving" />
                <q-btn label="Reset" type="reset" color="primary" flat />
              </div>
            </q-form>
          </q-tab-panel>
        </q-tab-panels>
      </template>
    </q-splitter>

    <!-- Phones: the list is the whole page, the form opens as a full screen sheet. -->
    <div v-else class="q-pa-md tree-pane">
      <q-input
        v-if="tree.length > 1"
        v-model="query"
        dense
        outlined
        clearable
        placeholder="Search"
        class="q-mb-md"
      >
        <template v-slot:prepend>
          <q-icon name="search" />
        </template>
      </q-input>

      <div v-if="!loading && tree.length <= 1" class="text-grey-7 q-mb-md">
        {{ emptyMessage }}
      </div>
      <div v-else-if="!visibleTree.length" class="text-grey-7">Nothing matches “{{ query }}”.</div>

      <q-tree
        :key="treeVersion"
        :nodes="visibleTree"
        node-key="id"
        selected-color="primary"
        :selected="selected"
        v-model:expanded="expanded"
        @update:selected="onSelect"
      >
        <template v-slot:default-header="prop">
          <div class="row items-center">
            <span>{{ prop.node.id }} - {{ prop.node.label }}</span>
          </div>
        </template>
      </q-tree>
    </div>

    <q-dialog
      v-model="dialog"
      :maximized="$q.screen.lt.md"
      transition-show="slide-up"
      transition-hide="slide-down"
    >
      <q-card class="dialog-card column no-wrap">
        <q-toolbar class="dialog-header q-px-md">
          <q-toolbar-title class="text-subtitle1 text-weight-medium">
            {{ entityLabel }} {{ entryId > 0 ? `#${entryId}` : '· new' }}
          </q-toolbar-title>
          <q-btn icon="close" flat round dense v-close-popup aria-label="Close" />
        </q-toolbar>

        <q-form @submit="emit('submit')" @reset="emit('reset')" class="col column no-wrap">
          <q-card-section class="col q-pt-sm dialog-body">
            <slot />
          </q-card-section>

          <div class="dialog-actions row items-center justify-between q-px-md q-py-sm">
            <q-btn
              v-if="entryId > 0"
              flat
              label="Delete"
              color="negative"
              type="button"
              @click="emit('delete')"
            />
            <div class="row q-gutter-sm">
              <q-btn label="Reset" color="secondary" flat type="reset" />
              <q-btn label="Save" color="primary" type="submit" :loading="saving" />
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

/**
 * The master-detail shell shared by the inventory and the configuration pages: a tree on the left,
 * a form per node on the right, the same form in a full-screen sheet on phones. Only the fields
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
  }>(),
  { entityLabel: 'Item' },
);

const emit = defineEmits<{
  select: [id: number];
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
  min-height: 44px; /* comfortable tap target on a phone */
  align-items: center;
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
}

/* The save button must stay reachable on a phone, whatever the form length. */
.dialog-actions {
  border-top: 1px solid rgba(0, 0, 0, 0.08);
  background: #fff;
  padding-bottom: calc(10px + env(safe-area-inset-bottom));
}
</style>
