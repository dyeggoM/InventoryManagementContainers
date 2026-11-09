<template>
  <q-page>
    <q-splitter v-model="splitterModel" v-if="$q.screen.gt.sm">
      <template v-slot:before>
        <div class="q-pa-md">
          <q-tree
            :nodes="tree"
            node-key="id"
            selected-color="primary"
            v-model:selected="selected"
            @update:selected="(target) => selectData(target)"
            default-expand-all
          >
            <template v-slot:default-header="prop">
              <div class="row items-center">
                <q-icon :name="prop.node.icon" />
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
          <q-tab-panel v-for="item in panels" :key="item.id" :name="item.id">
            <div class="text-h4 q-mb-md">Id: {{ item.id }}</div>
            <q-form @submit="sendTreeItem" @reset="resetTreeItem" class="q-gutter-md">
              <q-input label="label" v-model="treeItem.label" :rules="[labelRule]" />
              <!-- <q-input label="icon" v-model="treeItem.icon" /> -->
              <q-select
                label="parentId"
                class="col-5"
                v-model="treeItem.parentId"
                :options="createSelectOptions(tree, item.id)"
                :option-label="
                  (opt) =>
                    Object(opt) === opt && 'label' in opt && opt.label == 'Add New'
                      ? 'No Parent'
                      : opt.label
                "
                :option-value="
                  (opt) =>
                    Object(opt) === opt && 'value' in opt && opt.value == 0 ? null : opt.value
                "
                map-options
                emit-value
              />
              <!-- <q-input label="parentId" v-model="treeItem.parentId" /> -->
              <q-input label="group" v-model="treeItem.group" />
              <q-input label="description" v-model="treeItem.description" type="textarea" />
              <q-input label="sortOrder" v-model="treeItem.sortOrder" />
              <div>
                <q-btn label="Submit" type="submit" color="primary" />
                <q-btn label="Reset" type="reset" color="primary" flat class="q-ml-sm" />
                <q-btn
                  v-if="treeItem.id > 0"
                  label="Delete"
                  type="button"
                  color="negative"
                  flat
                  class="q-ml-sm"
                  @click="deleteTreeItem"
                />
              </div>
            </q-form>
          </q-tab-panel>
        </q-tab-panels>
      </template>
    </q-splitter>

    <div v-else class="q-pa-md">
      <q-tree
        :nodes="tree"
        node-key="id"
        selected-color="primary"
        v-model:selected="selected"
        @update:selected="(target) => selectData(target)"
        class="text-h5 text-weight-light"
        default-expand-all
      >
        <template v-slot:default-header="prop">
          <div class="row items-center">
            <!-- <q-icon :name="prop.node.icon" size="xl" /> -->
            <div>
              <span>{{ prop.node.id }} - {{ prop.node.label }}</span>
            </div>
          </div>
        </template>
      </q-tree>
    </div>

    <q-dialog
      v-model="showMobileDialog"
      :maximized="$q.screen.lt.md"
      transition-show="slide-up"
      transition-hide="slide-down"
    >
      <q-card :style="$q.screen.lt.md ? '' : 'min-width: 400px'">
        <q-card-section v-if="treeItem">
          <div class="text-h4 q-mb-md">Id: {{ treeItem.id }}</div>
          <q-form @submit="submitDialog" @reset="resetTreeItem" class="q-gutter-md">
            <q-input label="label" v-model="treeItem.label" :rules="[labelRule]" />
            <q-select
              label="parentId"
              class="col-5"
              v-model="treeItem.parentId"
              :options="createSelectOptions(tree, treeItem.id)"
              :option-label="
                (opt) =>
                  Object(opt) === opt && 'label' in opt && opt.label == 'Add New'
                    ? 'No Parent'
                    : opt.label
              "
              :option-value="
                (opt) =>
                  Object(opt) === opt && 'value' in opt && opt.value == 0 ? null : opt.value
              "
              map-options
              emit-value
            />
            <q-input label="group" v-model="treeItem.group" />
            <q-input label="description" v-model="treeItem.description" type="textarea" />
            <q-input label="sortOrder" v-model="treeItem.sortOrder" />
            <q-card-actions align="between" class="q-px-none q-mt-md">
              <q-btn flat label="Cancel" color="negative" type="button" v-close-popup />
              <q-btn
                v-if="treeItem.id > 0"
                flat
                label="Delete"
                color="negative"
                type="button"
                @click="deleteTreeItem"
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
import { ref, onMounted } from 'vue';
import { constantsService } from '@services/constants';
import { itemsService } from '@services/items';
import type { Constants } from '@services/constants';
import { useFeedback } from '../../composables/useFeedback';

const feedback = useFeedback();

const labelRule = (value: string) => (value && value.trim().length > 0) || 'Label is required';

const createItem = {
  id: 0,
  label: 'Add New',
  icon: '',
  children: [],
  sortOrder: 0,
  group: '',
  parentId: null,
  description: '',
  parent: undefined,
};

const treeItem = ref<Constants>({
  id: 0,
  label: '',
  icon: '',
  children: [],
  sortOrder: 0,
  group: '',
  parentId: null,
  description: '',
  parent: undefined,
});

const splitterModel = ref(30);
const selected = ref(0);
const tree = ref<Constants[]>([
  {
    label: 'Relax Hotel',
    id: 0,
    icon: '',
    sortOrder: 0,
    children: [
      {
        label: 'Food',
        id: 1,
        icon: 'restaurant_menu',
        sortOrder: 0,
        children: [
          {
            label: 'Test',
            id: 2,
            icon: '',
            sortOrder: 0,
            children: [],
          },
          {
            label: 'Test 2',
            id: 3,
            icon: '',
            sortOrder: 0,
            children: [],
          },
        ],
      },
      {
        label: 'Room service',
        id: 4,
        icon: 'room_service',
        sortOrder: 0,
        children: [],
      },
      {
        label: 'Room view',
        id: 5,
        icon: 'photo',
        sortOrder: 0,
        children: [],
      },
    ],
  },
]);
const panels = ref<Constants[]>([
  {
    label: 'Relax Hotel',
    id: 0,
    icon: '',
    sortOrder: 0,
    children: [],
  },
  {
    label: 'Food',
    id: 1,
    icon: 'restaurant_menu',
    sortOrder: 0,
    children: [],
  },
  {
    label: 'Test',
    id: 2,
    icon: '',
    sortOrder: 0,
    children: [],
  },
  {
    label: 'Test 2',
    id: 3,
    icon: '',
    sortOrder: 0,
    children: [],
  },
  {
    label: 'Room service',
    id: 4,
    icon: 'room_service',
    sortOrder: 0,
    children: [],
  },
  {
    label: 'Room view',
    id: 5,
    icon: 'photo',
    sortOrder: 0,
    children: [],
  },
]);

const loading = ref(false);
const saving = ref(false);

const showMobileDialog = ref(false);

async function loadData() {
  loading.value = true;

  try {
    const [constants, items] = await Promise.all([
      constantsService.getAll(),
      itemsService.getAll(),
    ]);

    if (constants != null && constants != undefined) {
      if (constants.tree != null && constants.tree != undefined) {
        tree.value = constants.tree;
        tree.value.unshift(createItem);
      }
      if (constants.panels != null && constants.panels != undefined) {
        panels.value = constants.panels;
        panels.value.unshift(createItem);
      }
    }
    console.log(items);
  } catch (error) {
    feedback.error(error, 'Could not load the constants');
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  void loadData();
});

function selectData(target: number) {
  const data = findConstantById(tree.value, target);
  if (data != undefined) {
    treeItem.value.id = data.id;
    treeItem.value.label = data.label;
    treeItem.value.icon = data.icon;
    treeItem.value.children = data.children;
    treeItem.value.sortOrder = data.sortOrder;
    treeItem.value.group = data.group;
    treeItem.value.parentId = data.parentId;
    treeItem.value.description = data.description;
    treeItem.value.parent = data.parent;
  }

  if (window.innerWidth < 768) {
    showMobileDialog.value = true;
  }
}

function findConstantById(constants: Constants[], id: number): Constants | undefined {
  for (const data of constants) {
    if (data.id === id) {
      return data;
    }

    const found = findConstantById(data.children, id);

    if (found) {
      return found;
    }
  }

  return undefined;
}

async function sendTreeItem(): Promise<boolean> {
  const id = treeItem.value.id;
  saving.value = true;
  try {
    if (id > 0) {
      await constantsService.update(id, treeItem.value);
      feedback.success('Constant updated');
    } else {
      await constantsService.create(treeItem.value);
      feedback.success('Constant created');
    }
    resetTreeItem();
    await loadData();
    return true;
  } catch (error) {
    feedback.error(error, 'Could not save the constant');
    return false;
  } finally {
    saving.value = false;
  }
}

async function submitDialog() {
  if (await sendTreeItem()) {
    showMobileDialog.value = false;
  }
}

async function deleteTreeItem() {
  const id = treeItem.value.id;
  if (id <= 0) return;
  const confirmed = await feedback.confirmDelete(
    `Delete "${treeItem.value.label || id}"? Its children are deleted with it.`,
  );
  if (!confirmed) return;
  try {
    await constantsService.delete(id);
    feedback.success('Constant deleted');
    resetTreeItem();
    await loadData();
  } catch (error) {
    feedback.error(error, 'Could not delete the constant');
  }
}

function resetTreeItem() {
  treeItem.value = {
    id: 0,
    label: '',
    icon: '',
    children: [],
    sortOrder: 0,
    group: '',
    parentId: null,
    description: '',
    parent: undefined,
  };
}

function createSelectOptions(constants: Constants[], id: number) {
  const flattened = constantsService.flattenConstants(constants);

  const mappedFlattened = flattened
    .filter((x) => x.id != id || id == 0)
    .map((item) => ({
      value: item.id,
      label: item.hierarchicalLabel,
      // Optional: add indentation for better visual hierarchy
      displayLabel: `${'  '.repeat(item.depth)}${item.label}`,
      depth: item.depth,
    }));
  console.log(mappedFlattened);
  return mappedFlattened;
}
</script>
