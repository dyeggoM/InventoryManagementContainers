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
              <q-input label="label" v-model="treeItem.label" />
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
          <q-form @submit="sendTreeItem" @reset="resetTreeItem" class="q-gutter-md">
            <q-input label="label" v-model="treeItem.label" />
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
          </q-form>
        </q-card-section>

        <q-card-actions align="between">
          <q-btn flat label="Cancel" color="negative" v-close-popup />
          <q-btn flat label="Reset" color="secondary" @click="resetTreeItem" />
          <q-btn flat label="Submit" color="primary" @click="sendTreeItem" v-close-popup />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { constantsService } from '@services/constants';
import { itemsService } from '@services/items';
import type { Constants } from '@services/constants';

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
    console.error(error);
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

async function sendTreeItem() {
  const id = treeItem.value.id;
  if (id > 0) {
    const [constantsUpdate] = await Promise.all([constantsService.update(id, treeItem.value)]);
    console.log(constantsUpdate);
    resetTreeItem();
    await Promise.all([loadData()]);
    //TODO: Get constant from DB and assign the values to current Tree and Panels
    // const [constantsGet] = await Promise.all([
    //   constantsService.getById(id)
    // ]);
    // console.log(constantsGet)
  } else if (id == 0) {
    const [constantsCreate] = await Promise.all([constantsService.create(treeItem.value)]);
    console.log(constantsCreate);
    resetTreeItem();
    await Promise.all([loadData()]);
    //TODO: Get constant from DB and assign the values to current Tree and Panels
    // const [constantsGet] = await Promise.all([
    //   constantsService.getById(id)
    // ]);
    // console.log(constantsGet)
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
