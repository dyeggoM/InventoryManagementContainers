<template>
  <q-page class="imc-page">
    <q-linear-progress v-if="loading" indeterminate color="primary" />

    <!-- Signed in as a role that may not write, or impersonating somebody: say so on both layouts. -->
    <q-banner v-if="readonly" dense class="bg-orange-1 text-orange-10">
      <template v-slot:avatar><q-icon name="visibility" /></template>
      Solo lectura: tu rol no permite crear, editar ni borrar.
    </q-banner>

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
              v-if="!readonly"
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
                    {{ prettyLabel(prop.node.match.label) }} · {{ shortValue(prop.node.match.value) }}
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
              <q-btn v-if="panel.id > 0 && !readonly" flat round dense icon="more_vert" aria-label="More actions">
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
              <div v-if="!readonly" class="row q-gutter-sm q-mt-md">
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
          v-if="!readonly"
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
                {{ prettyLabel(prop.node.match.label) }} · {{ shortValue(prop.node.match.value) }}
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

      <q-page-sticky v-if="!readonly" position="bottom-right" :offset="[16, 16]">
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
            v-if="entryId > 0 && !readonly"
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
              <q-btn v-if="!readonly" label="Reset" color="grey-9" flat type="reset" />
              <q-btn v-if="!readonly" label="Save" color="primary" type="submit" :loading="saving" class="q-px-lg" />
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
import { countMatches, filterTree, shortValue } from '../services/search';

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
    /**
     * The session may not write (the lector role, or a superadmin impersonating somebody): every
     * create, duplicate, delete and save affordance is hidden, and the pages disable their fields.
     */
    readonly?: boolean;
  }>(),
  { entityLabel: 'Item', entryLabel: '', dirty: false, readonly: false },
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

function collectIds(nodes: TreeEntry[], acc: number[] = []): number[] {
  for (const node of nodes) {
    if ((node.children ?? []).length > 0) {
      acc.push(node.id);
      collectIds(node.children ?? [], acc);
    }
  }
  return acc;
}

const visibleTree = computed(() => {
  const needle = query.value.trim().toLowerCase();
  return needle ? filterTree(props.tree, needle) : props.tree;
});

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
  color: var(--imc-text-muted);
}

.imc-count {
  min-width: 22px;
  padding: 0 6px;
  border-radius: 11px;
  background: var(--imc-surface-sunken);
  color: var(--imc-text-muted);
  font-size: 12px;
  line-height: 20px;
  text-align: center;
}

.imc-chevron {
  color: var(--imc-text-soft);
}

.imc-attach {
  color: var(--imc-text-soft);
  margin-left: 6px;
}

.imc-unsaved {
  color: var(--imc-warning-text);
  font-weight: 500;
}

/* Sibling rows get a hairline; a parent row is tinted so the groups read as blocks. */
.imc-tree :deep(.q-tree__node + .q-tree__node) {
  border-top: 1px solid var(--imc-border);
}

.imc-tree :deep(.q-tree__node--parent > .q-tree__node-header) {
  background: var(--imc-surface-raised);
}

.imc-fab {
  padding-left: 18px;
  padding-right: 22px;
}

.tree-pane :deep(.q-tree__arrow) {
  padding: 8px 4px; /* the chevron is a target too */
  color: var(--imc-text-soft);
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
  border-bottom: 1px solid var(--imc-hairline);
  padding-top: calc(4px + env(safe-area-inset-top));
}

.dialog-body {
  overflow-y: auto;
  padding-bottom: 32px; /* the last row must clear the fixed footer */
}

/* Save stays reachable on a phone, whatever the form length. */
.dialog-actions {
  border-top: 1px solid var(--imc-hairline);
  background: var(--imc-surface);
  padding-bottom: calc(10px + env(safe-area-inset-bottom));
}
</style>