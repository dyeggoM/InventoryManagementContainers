import { beforeEach, describe, expect, it, vi } from 'vitest';

// The composable asks Quasar for the screen size and for notifications; both are replaced here so
// the state machine can be exercised without mounting anything.
const { screen, feedback } = vi.hoisted(() => ({
  screen: { lt: { md: false } },
  feedback: {
    success: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
    confirmDelete: vi.fn(),
    confirmDiscard: vi.fn(),
  },
}));

vi.mock('quasar', () => ({ useQuasar: () => ({ screen }) }));
vi.mock('./useFeedback', () => ({ useFeedback: () => feedback }));

import { useTreeEditor, type TreeEntry } from './useTreeEditor';

interface Row extends TreeEntry {
  attributes?: string;
}

interface Captured {
  duplicate?: Row;
  select?: Row;
  reset: number;
}

function makeEditor(captured: Captured) {
  const service = {
    getAll: vi.fn(() => Promise.resolve({
      tree: [
        {
          id: 1,
          label: 'Kit',
          icon: 'drum',
          children: [{ id: 2, label: 'Part', icon: '', children: [], parentId: 1 }],
        },
      ],
      panels: [],
    })),
    create: vi.fn(() => Promise.resolve(undefined)),
    update: vi.fn(() => Promise.resolve(undefined)),
    delete: vi.fn(() => Promise.resolve(undefined)),
  };

  const editor = useTreeEditor<Row>({
    service,
    addNew: () => ({ id: 0, label: 'Add New', icon: '', children: [] }),
    blank: () => ({ id: 0, label: '', icon: '', children: [], parentId: null }),
    flatten: (nodes) =>
      [{ id: 0, label: 'Add New', hierarchicalLabel: 'Add New', depth: 0 }].concat(
        nodes.flatMap((node) => [
        {
          id: node.id,
          label: node.label,
          hierarchicalLabel: node.label,
          depth: 0,
        },
          ...(node.children ?? []).map((child) => ({
            id: child.id,
            label: child.label,
            hierarchicalLabel: `Kit / ${child.label}`,
            depth: 1,
          })),
        ]),
      ),
    entity: 'item',
    emptyMessage: 'No items yet',
    deleteHint: 'Its files go with it.',
    onSelect: (entry) => {
      captured.select = entry;
    },
    onReset: () => {
      captured.reset += 1;
    },
    onDuplicate: (source) => {
      captured.duplicate = source;
    },
    dirtySnapshot: (entry) => JSON.stringify(entry),
  });

  return { editor, service };
}

function newCaptured(): Captured {
  return { reset: 0 };
}

describe('useTreeEditor', () => {
  beforeEach(() => {
    screen.lt.md = false;
    Object.values(feedback).forEach((fn) => fn.mockReset());
  });

  it('is not dirty before anything is loaded or typed', () => {
    const { editor } = makeEditor(newCaptured());
    expect(editor.isDirty.value).toBe(false);
  });

  it('loads a tree, expands what has children and appends the empty entry to the panels', async () => {
    const { editor } = makeEditor(newCaptured());
    await editor.loadData();

    expect(editor.tree.value).toHaveLength(1);
    expect(editor.expanded.value).toEqual([1]);
    expect(editor.panels.value.at(-1)?.id).toBe(0);
  });

  it('marks a loaded record clean and an edit dirty, and clears it again', async () => {
    const { editor } = makeEditor(newCaptured());
    await editor.loadData();
    await editor.selectData(1);

    expect(editor.isDirty.value).toBe(false);

    editor.treeItem.value.label = 'Renamed';
    await Promise.resolve();
    expect(editor.isDirty.value).toBe(true);

    editor.markClean();
    expect(editor.isDirty.value).toBe(false);
  });

  it('opens the sheet when a record is selected on a phone', async () => {
    screen.lt.md = true;
    const { editor } = makeEditor(newCaptured());
    await editor.loadData();
    await editor.selectData(2);

    expect(editor.showMobileDialog.value).toBe(true);
    expect(editor.treeItem.value.label).toBe('Part');
  });

  it('starts a child form parented to the open record, and clean', async () => {
    const { editor } = makeEditor(newCaptured());
    await editor.loadData();
    await editor.selectData(1);

    editor.createChildEntry();

    expect(editor.treeItem.value.id).toBe(0);
    expect(editor.treeItem.value.parentId).toBe(1);
    expect(editor.isDirty.value).toBe(false);
  });

  it('does not offer a child for a record that does not exist yet', () => {
    const { editor } = makeEditor(newCaptured());
    editor.createChildEntry();
    expect(editor.treeItem.value.parentId).toBeNull();
  });

  it('duplicates the open record: same parent and icon, a copy name, no files', async () => {
    const captured = newCaptured();
    const { editor } = makeEditor(captured);
    await editor.loadData();
    await editor.selectData(2);

    editor.duplicateEntry();

    expect(captured.duplicate?.id).toBe(2);
    expect(editor.treeItem.value.label).toBe('Part (copy)');
    expect(editor.treeItem.value.parentId).toBe(1);
    expect(editor.treeItem.value.id).toBe(0);
  });

  it('leaves the current record out of its own parent list', async () => {
    const { editor } = makeEditor(newCaptured());
    await editor.loadData();
    await editor.selectData(2);

    const options = editor.parentOptions().map((option) => option.value);

    expect(options).toContain(null);
    expect(options).not.toContain(2);
    expect(options).toContain(1);
  });

  it('refuses to save when validation returns a message', async () => {
    const { service } = makeEditor(newCaptured());

    // A controller that always complains, to prove the save never reaches the service.
    const strict = useTreeEditor<Row>({
      service,
      addNew: () => ({ id: 0, label: 'Add New', icon: '', children: [] }),
      blank: () => ({ id: 0, label: '', icon: '', children: [], parentId: null }),
      flatten: () => [],
      entity: 'item',
      emptyMessage: '',
      deleteHint: '',
      validate: () => 'Give every attribute a name',
    });

    strict.treeItem.value.label = 'Valid';
    const saved = await strict.sendTreeItem();

    expect(saved).toBe(false);
    expect(service.create).not.toHaveBeenCalled();
    expect(feedback.error).toHaveBeenCalledWith('Give every attribute a name');
  });

  it('deletes only after the confirmation is accepted', async () => {
    const { editor, service } = makeEditor(newCaptured());
    await editor.loadData();
    await editor.selectData(1);

    feedback.confirmDelete.mockResolvedValueOnce(false);
    await editor.deleteTreeItem();
    expect(service.delete).not.toHaveBeenCalled();

    feedback.confirmDelete.mockResolvedValueOnce(true);
    await editor.deleteTreeItem();
    expect(service.delete).toHaveBeenCalledWith(1);
  });
});
