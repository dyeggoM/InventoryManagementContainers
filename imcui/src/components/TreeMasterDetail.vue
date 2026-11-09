<template>
  <q-page>
    <q-linear-progress v-if="loading" indeterminate color="primary" />

    <q-splitter v-model="splitter" v-if="$q.screen.gt.sm">
      <template v-slot:before>
        <div class="q-pa-md">
          <div v-if="!loading && tree.length <= 1" class="text-grey-7 q-mb-md">
            {{ emptyMessage }}
          </div>
          <q-tree
            :key="treeVersion"
            :nodes="tree"
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

    <div v-else class="q-pa-md">
      <div v-if="!loading && tree.length <= 1" class="text-grey-7 q-mb-md">
        {{ emptyMessage }}
      </div>
      <q-tree
        :key="treeVersion"
        :nodes="tree"
        node-key="id"
        selected-color="primary"
        class="text-h5 text-weight-light"
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
      <q-card :style="$q.screen.lt.md ? '' : 'min-width: 400px'">
        <q-card-section>
          <div class="text-h4 q-mb-md">Id: {{ entryId }}</div>
          <q-form @submit="emit('submit')" @reset="emit('reset')" class="q-gutter-md">
            <slot />
            <q-card-actions align="between" class="q-px-none q-mt-md">
              <q-btn flat label="Cancel" color="negative" type="button" v-close-popup />
              <q-btn
                v-if="entryId > 0"
                flat
                label="Delete"
                color="negative"
                type="button"
                @click="emit('delete')"
              />
              <q-btn label="Reset" color="secondary" flat type="reset" />
              <q-btn label="Submit" color="primary" type="submit" :loading="saving" />
            </q-card-actions>
          </q-form>
        </q-card-section>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup lang="ts">
import type { TreeEntry } from '../composables/useTreeEditor';

/**
 * The master-detail shell shared by the inventory and the configuration pages: a tree on the left,
 * a form per node on the right, the same form in a dialog on small screens. Only the fields inside
 * the form belong to the page (the default slot).
 */
defineProps<{
  tree: TreeEntry[];
  panels: TreeEntry[];
  /** Id of the node currently in the form; drives the dialog title and the delete affordance. */
  entryId: number;
  treeVersion: number;
  loading: boolean;
  saving: boolean;
  emptyMessage: string;
}>();

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

// q-tree emits null when the selection is cleared (clicking the selected node again). Turning
// that into a null model hides every tab panel, so the last selection is kept instead.
function onSelect(id: number | null) {
  if (typeof id !== 'number') return;
  selected.value = id;
  emit('select', id);
}
</script>