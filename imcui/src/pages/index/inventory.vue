<template>
  <tree-master-detail
    v-model:selected="selected"
    v-model:expanded="expanded"
    v-model:splitter="splitterModel"
    v-model:dialog="showMobileDialog"
    :tree="tree"
    :panels="panels"
    :entry-id="treeItem.id"
    :tree-version="treeVersion"
    :loading="loading"
    :saving="saving"
    :empty-message="emptyMessage"
    :dirty="isDirty"
    :readonly="!canWrite"
    entity-label="Item"
    :entry-label="treeItem.label"
    secondary-action-label="New set"
    @select="selectData"
    @create="createEntry"
    @secondary-action="createSet"
    @submit="submitDialog"
    @reset="resetTreeItem"
    @delete="deleteTreeItem"
    @duplicate="duplicateEntry"
  >
    <item-editor
      v-model:label="treeItem.label"
      v-model:parent="treeItem.parentId"
      v-model:attributes="attributes"
      :parent-options="parentOptions()"
      :label-rule="labelRule"
      :readonly="!canWrite"
      :attribute-labels="attributeLabels"
      :attribute-lists="attributeLists"
      :attribute-types="attributeTypes"
      :create-value="canManageConfiguration ? createAttributeValue : undefined"
    >
      <template v-slot:details-actions>
        <q-btn
          v-if="treeItem.id > 0 && canWrite"
          flat
          no-caps
          icon="add"
          label="Add child item"
          color="primary"
          class="q-mb-xs"
          @click="createChildEntry"
        />
      </template>
    </item-editor>

    <!--
      A set's pieces are its children: the tree already counts them, this is where they are chosen.
      Shown only for a set, because on any other item a child is a sub-item and the action for that
      is "Add child item" in the details header.
    -->
    <set-pieces
      v-if="isSet && treeItem.id > 0"
      :pieces="pieces"
      :candidates="candidates"
      :readonly="!canWrite"
      :busy="piecesBusy"
      @add="attachPieces"
      @remove="removePiece"
    />

    <div class="imc-section">
      <div class="imc-section__title">Attachments</div>
      <attachment-editor
        v-model="attachments"
        :types="attachmentTypes"
        :readonly="!canWrite"
        @add="addAttachment"
        @remove="removeAttachment"
      />
    </div>
  </tree-master-detail>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import TreeMasterDetail from '../../components/TreeMasterDetail.vue';
import ItemEditor from '../../components/ItemEditor.vue';
import SetPieces from '../../components/SetPieces.vue';
import AttachmentEditor, { type AttachmentRow } from '../../components/AttachmentEditor.vue';
import { useTreeEditor } from '../../composables/useTreeEditor';
import { useAttributeCatalogue } from '../../composables/useAttributeCatalogue';
import { useFeedback } from '../../composables/useFeedback';
import { useAuthStore } from '../../stores/auth-store';
import { itemsService, type Item, type Attributes } from '@services/items';
import { attachmentsService } from '@services/attachment';
import { constantsService } from '@services/constants';

const feedback = useFeedback();
const auth = useAuthStore();

// A lector reads; an operador writes. A superadmin impersonating somebody writes nothing.
const canWrite = computed(() => auth.canWrite);

const attributes = ref<Attributes[]>([]);
// The attribute half of Configuration is shared with the import's row correction, so it is read
// through one composable instead of two copies of the same rules.
const {
  labels: attributeLabels,
  lists: attributeLists,
  types: attributeTypes,
  load: loadCatalogue,
  createValue: createAttributeValue,
} = useAttributeCatalogue({
  onRefreshError: (error) => feedback.error(error, 'Could not load the constants'),
});
const attachmentTypes = ref<string[]>([]);
const attachments = ref<AttachmentRow[]>([]);

// Only an admin and a superadmin may write Configuration, and never while impersonating.
const canManageConfiguration = computed(() => auth.canManageConfiguration);

const blank = (): Item => ({
  id: 0,
  label: '',
  icon: '',
  children: [],
  parentId: null,
  attributes: '{}',
  attachments: [],
  parent: undefined,
});

function blankAttributes(): Attributes[] {
  return attributeLabels.value.map((label) => ({ label, value: '' }));
}

/**
 * The rows an entry produces: every catalogue attribute, plus any key the record carries that the
 * catalogue does not define (a value typed on this item alone must survive a reload).
 */
function rowsFrom(raw: string | null | undefined): Attributes[] {
  const parsed = parseAttributes(raw);
  const rows = attributeLabels.value.map((label) => ({ label, value: parsed[label] ?? '' }));
  const extra = Object.keys(parsed).filter((key) => !attributeLabels.value.includes(key));
  return [...rows, ...extra.map((key) => ({ label: key, value: parsed[key] ?? '' }))];
}

const {
  tree,
  panels,
  treeItem,
  splitterModel,
  selected,
  expanded,
  loading,
  saving,
  treeVersion,
  showMobileDialog,
  isDirty,
  emptyMessage,
  labelRule,
  loadData,
  selectData,
  resetTreeItem,
  createEntry,
  createChildEntry,
  duplicateEntry,
  submitDialog,
  deleteTreeItem,
  parentOptions,
} = useTreeEditor<Item>({
  service: itemsService,
  addNew: () => ({ ...blank(), label: 'Add New' }),
  blank,
  flatten: (nodes) => itemsService.flattenItems(nodes),
  entity: 'item',
  emptyMessage: 'No items yet',
  deleteHint: 'Its attachments are deleted with it.',
  splitter: 34,
  // The entry alone does not describe the form: the attributes and the attachment rows live here.
  dirtySnapshot: (entry) =>
    JSON.stringify({
      entry,
      attributes: attributes.value,
      attachments: attachments.value.map((row) => [
        row.id,
        row.type ?? '',
        row.fileName ?? '',
        row.file ? 'picked' : '',
      ]),
    }),
  onSelect: async (entry) => {
    if (entry.id > 0) {
      attributes.value = rowsFrom(entry.attributes);
      await loadAttachments(entry.id);
    } else {
      attributes.value = blankAttributes();
      attachments.value = [];
    }
  },
  onDuplicate: (source) => {
    attributes.value = rowsFrom(source.attributes);
    // Files are not copied: the new record points at nothing until it is given its own.
    attachments.value = [];
  },
  onReset: () => {
    attributes.value = blankAttributes();
    attachments.value = [];
  },
  validate: () => {
    if (attributes.value.some((row) => !row.label.trim() && row.value.trim())) {
      return 'Give every attribute a name';
    }
    const keys = attributes.value.map((row) => row.label.trim().toLowerCase()).filter(Boolean);
    if (new Set(keys).size !== keys.length) return 'Two attributes have the same name';
    for (const attachment of attachments.value.filter((row) => row.id === 0)) {
      if (!attachment.fileName) return 'Every new attachment needs a file';
      if (!attachment.type) return 'Every new attachment needs a type';
    }
    return null;
  },
  beforeSave: (entry) => {
    // Attachments live in their own table now: a JSON payload must not carry megabytes of base64
    // for a photo that is written to disk as a file.
    entry.attachments = [];
  },
  afterSave: async (id) => {
    const pending = attachments.value.filter((row) => row.id === 0 && row.file);
    if (!pending.length || id <= 0) return;

    for (const row of pending) {
      const file = row.file;
      if (!file) continue;
      try {
        await attachmentsService.upload(id, file, row.type ?? null);
      } catch (error) {
        // One bad file must not lose the record that was just saved.
        feedback.error(error, `Could not upload ${row.fileName || 'the file'}`);
      }
    }
  },
});

/** The attribute value that makes an item a set; its values come from the `Type` group. */
const SET_TYPE = 'Set';

/** The Type attribute of the record in the form, which is what tells a set from any other item. */
const isSet = computed(
  () => attributeValue(treeItem.value.attributes, 'Type').trim().toLowerCase() === 'set',
);

/**
 * The pieces come from the tree and not from the form's copy of the node: attaching one reloads the
 * tree, and reading it there is what keeps this list, the count badge and the form from disagreeing.
 */
const pieces = computed(() => {
  const id = treeItem.value.id;
  if (id <= 0) return [];
  return itemsService.findItem(tree.value, id)?.children ?? [];
});

/** The items this set may take: the tree's roots, minus the set itself. */
const candidates = computed(() => itemsService.rootCandidates(tree.value, treeItem.value.id));

const piecesBusy = ref(false);

/**
 * A set is created as one. Until now it took "new item" and then finding `Type` among the
 * attributes; here the name is the only question, the attribute is written with the record, and the
 * new node opens selected so its pieces are one tap away.
 */
async function createSet() {
  const label = await feedback.prompt(
    'New set',
    'Set name',
    `It is created with the attribute Type = ${SET_TYPE}. Add its pieces once it is open.`,
  );
  if (!label) return;

  try {
    const created = await itemsService.create({
      ...blank(),
      label,
      attributes: JSON.stringify({ Type: SET_TYPE }),
    });
    await loadData();
    // The list and the panels are the server's answer, so the new node is found there, not here.
    const node =
      (created?.id ? itemsService.findItem(tree.value, created.id) : undefined) ??
      tree.value.find((candidate) => candidate.label === label);
    if (!node) {
      feedback.error('The set was created but the tree did not return it; reload to see it.');
      return;
    }
    selected.value = node.id;
    await selectData(node.id);
    feedback.success('Set created');
  } catch (error) {
    feedback.error(error, 'Could not create the set');
  }
}

/** Attaches the picked items in one call, then reloads so the tree's count follows. */
async function attachPieces(ids: number[]) {
  const setId = treeItem.value.id;
  if (setId <= 0 || ids.length === 0) return;

  piecesBusy.value = true;
  try {
    const result = await itemsService.attachChildren(setId, ids);
    feedback.success(`${result.moved} ${result.moved === 1 ? 'piece' : 'pieces'} added`);
    await loadData();
  } catch (error) {
    feedback.error(error, 'Could not add the pieces');
  } finally {
    piecesBusy.value = false;
  }
}

/** Detaches one piece: it leaves the set and stays in the inventory. */
async function removePiece(childId: number) {
  const setId = treeItem.value.id;
  if (setId <= 0 || childId <= 0) return;

  piecesBusy.value = true;
  try {
    await itemsService.detachChild(setId, childId);
    feedback.success('Piece removed from the set');
    await loadData();
  } catch (error) {
    feedback.error(error, 'Could not remove the piece');
  } finally {
    piecesBusy.value = false;
  }
}

/**
 * Reads what the form is built from: the attribute catalogue — shared with the import's row
 * correction, which edits an item with the same fields — and the attachment types, which only this
 * screen needs.
 */
async function loadConstants() {
  try {
    const [, attachmentTypeRows] = await Promise.all([
      loadCatalogue(),
      constantsService.getByGroup('AttachmentType'),
    ]);
    attachmentTypes.value = (attachmentTypeRows ?? []).map((row) => row.label);
    // The catalogue names the rows of a blank item; a later refresh (a value added from the form)
    // must not go through here, or it would wipe what the user already typed.
    attributes.value = blankAttributes();
  } catch (error) {
    feedback.error(error, 'Could not load the constants');
  }
}

onMounted(async () => {
  // Constants first: the attribute form is built from them, and selecting a node needs them.
  await loadConstants();
  await loadData();
});

async function loadAttachments(id: number) {
  try {
    attachments.value = (await attachmentsService.getAllByItemId(id)) ?? [];
  } catch (error) {
    feedback.error(error, 'Could not load the attachments');
  }
}

/** The stored attributes as a map, or null when the stored value is not a JSON object. */
function readAttributes(raw: string | null | undefined): Record<string, string> | null {
  if (!raw) return {};
  try {
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, string>) : null;
  } catch {
    return null;
  }
}

/** One attribute of a record, for the decisions this page makes outside the form (is it a set?). */
function attributeValue(raw: string | null | undefined, label: string): string {
  return readAttributes(raw)?.[label] ?? '';
}

function parseAttributes(raw: string | null | undefined): Record<string, string> {
  const parsed = readAttributes(raw);
  if (parsed) return parsed;
  // Rows written before the serializer existed can hold invalid JSON; the form must still open.
  feedback.error('This item has malformed attributes; they were reset in the form.');
  return {};
}

function addAttachment() {
  attachments.value.push({
    id: 0,
    itemId: treeItem.value.id,
    // Photos are the common case on a phone, and the type decides which picker opens.
    type: attachmentTypes.value.includes('Photo') ? 'Photo' : null,
    fileName: null,
    file: null,
    previewUrl: null,
    attributes: null,
  });
}

async function removeAttachment(index: number) {
  const row = attachments.value[index];
  if (!row) return;
  if (row.id === 0) {
    attachments.value.splice(index, 1);
    return;
  }
  const confirmed = await feedback.confirmDelete(
    `Delete attachment "${row.fileName || row.id}"? This cannot be undone.`,
  );
  if (!confirmed) return;
  try {
    await attachmentsService.delete(row.id);
    feedback.success('Attachment deleted');
    await loadAttachments(row.itemId);
  } catch (error) {
    feedback.error(error, 'Could not delete the attachment');
  }
}

watch(
  attributes,
  () => {
    // Build the payload with a serializer, never by string concatenation: a value containing a
    // quote, backslash or newline used to produce invalid JSON that the API stored verbatim.
    const record: Record<string, string> = {};
    for (const attribute of attributes.value) {
      if (attribute.label) record[attribute.label] = attribute.value ?? '';
    }
    treeItem.value.attributes = JSON.stringify(record);
  },
  { deep: true },
);
</script>
