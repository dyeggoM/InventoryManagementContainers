import { ref, type Ref } from 'vue';
import { useQuasar } from 'quasar';
import { useFeedback } from './useFeedback';

/** The shape every node of a tree editor needs: what it is called and where it sits. */
export interface TreeEntry {
  id: number;
  label: string;
  icon?: string;
  children: TreeEntry[];
  parentId?: number | null | undefined;
}

export interface FlatEntry {
  id: number;
  label: string;
  hierarchicalLabel: string;
  depth: number;
}

/** The slice of a service a tree editor needs; the pages pass their own. */
export interface TreeService<T extends TreeEntry> {
  getAll(): Promise<{ tree: T[]; panels: T[] }>;
  create(item: T): Promise<unknown>;
  update(id: number, item: T): Promise<unknown>;
  delete(id: number): Promise<unknown>;
}

export interface TreeEditorOptions<T extends TreeEntry> {
  service: TreeService<T>;
  /** A node with the sentinel id 0: the "Add New" row every tree and panel list starts with. */
  addNew: () => T;
  /** An editable entry with the sentinel id, used to clear the form. */
  blank: () => T;
  flatten: (nodes: T[]) => FlatEntry[];
  entity: string;
  emptyMessage: string;
  deleteHint: string;
  splitter?: number;
  /** Runs after a node is copied into the form (load attachments, parse attributes, ...). */
  onSelect?: (entry: T) => void | Promise<void>;
  onReset?: () => void;
  /** Runs before a save, for payload shaping the service cannot know about. */
  beforeSave?: (entry: T) => void;
}

/**
 * The state and the four operations every master-detail tree page performs: load a tree and its
 * panels, select a node into the form, save it, delete it. Two pages duplicated all of this
 * line-for-line with only the service and the form fields differing.
 */
export function useTreeEditor<T extends TreeEntry>(options: TreeEditorOptions<T>) {
  const feedback = useFeedback();
  const $q = useQuasar();

  const tree = ref([]) as Ref<T[]>;
  const panels = ref([]) as Ref<T[]>;
  const treeItem = ref(options.blank()) as Ref<T>;
  const splitterModel = ref(options.splitter ?? 40);
  const selected = ref(-1);
  const expanded = ref<number[]>([]);
  const loading = ref(false);
  const saving = ref(false);
  const treeVersion = ref(0);
  const showMobileDialog = ref(false);

  function collectExpandable(nodes: TreeEntry[], acc: number[] = []): number[] {
    for (const node of nodes) {
      const children = node.children ?? [];
      if (children.length > 0) {
        acc.push(node.id);
        collectExpandable(children, acc);
      }
    }
    return acc;
  }

  function findById(nodes: TreeEntry[], id: number): TreeEntry | undefined {
    for (const node of nodes) {
      if (node.id === id) return node;
      const found = findById(node.children ?? [], id);
      if (found) return found;
    }
    return undefined;
  }

  async function loadData() {
    loading.value = true;
    try {
      const response = await options.service.getAll();
      tree.value = [...(response?.tree ?? []), options.addNew()];
      panels.value = [...(response?.panels ?? []), options.addNew()];
    } catch (error) {
      feedback.error(error, `Could not load the ${options.entity}s`);
    } finally {
      loading.value = false;
      expanded.value = collectExpandable(tree.value);
      // Re-create the tree with data present so expansion and node state start consistent.
      treeVersion.value += 1;
    }
  }

  async function selectData(id: number) {
    const node = findById(tree.value, id);
    if (node) {
      treeItem.value = Object.assign(options.blank(), node);
      await options.onSelect?.(treeItem.value);
    }
    if ($q.screen.lt.md) {
      showMobileDialog.value = true;
    }
  }

  function resetTreeItem() {
    treeItem.value = options.blank();
    selected.value = 0;
    options.onReset?.();
  }

  async function sendTreeItem(): Promise<boolean> {
    const id = treeItem.value.id;
    saving.value = true;
    try {
      options.beforeSave?.(treeItem.value);
      if (id > 0) {
        await options.service.update(id, treeItem.value);
        feedback.success(`${cap(options.entity)} updated`);
      } else {
        await options.service.create(treeItem.value);
        feedback.success(`${cap(options.entity)} created`);
      }
      resetTreeItem();
      await loadData();
      return true;
    } catch (error) {
      feedback.error(error, `Could not save the ${options.entity}`);
      return false;
    } finally {
      saving.value = false;
    }
  }

  async function submitDialog(): Promise<boolean> {
    if (await sendTreeItem()) {
      showMobileDialog.value = false;
      return true;
    }
    return false;
  }

  async function deleteTreeItem() {
    const id = treeItem.value.id;
    if (id <= 0) return;
    const confirmed = await feedback.confirmDelete(
      `Delete "${treeItem.value.label || id}"? ${options.deleteHint}`,
    );
    if (!confirmed) return;
    try {
      await options.service.delete(id);
      feedback.success(`${cap(options.entity)} deleted`);
      resetTreeItem();
      await loadData();
    } catch (error) {
      feedback.error(error, `Could not delete the ${options.entity}`);
    }
  }

  function parentOptions(): { value: number | null; label: string; displayLabel: string; depth: number }[] {
    return options
      .flatten(tree.value)
      .filter((entry) => entry.id != treeItem.value.id || treeItem.value.id === 0)
      .map((entry) => ({
        // The sentinel node means "no parent"; the select emits it as null.
        value: entry.id === 0 ? null : entry.id,
        label: entry.id === 0 ? 'No Parent' : entry.hierarchicalLabel,
        displayLabel: `${'  '.repeat(entry.depth)}${entry.label}`,
        depth: entry.depth,
      }));
  }

  const labelRule = (value: string) => (value && value.trim().length > 0) || 'Label is required';

  return {
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
    emptyMessage: options.emptyMessage,
    labelRule,
    loadData,
    selectData,
    resetTreeItem,
    sendTreeItem,
    submitDialog,
    deleteTreeItem,
    parentOptions,
  };
}

function cap(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
