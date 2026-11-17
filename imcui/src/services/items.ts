import { api } from './api';
import type { Attachment } from './attachment';

export interface Item {
  id: number;
  label: string;
  icon: string;
  children: Item[];
  parentId?: number | null | undefined;
  attributes?: string | undefined;
  parent?: Item | undefined;
  attachments?: Attachment[] | undefined;
  /** Filled by the tree response so the list can mark records that carry files. */
  attachmentCount?: number | undefined;
}

export interface ItemReponse {
  tree: Item[];
  panels: Item[];
}

export interface Attributes {
  label: string;
  value: string;
}

export interface FlattenedItem {
  id: number;
  label: string;
  hierarchicalLabel: string;
  depth: number;
  originalItem: Item;
}

/** What `POST /item/{id}/children` answers: how many items that call attached. */
export interface MoveChildrenResult {
  moved: number;
}

export const itemsService = {
  getAll(): Promise<ItemReponse> {
    return api.get<ItemReponse>('/item');
  },

  getById(id: number): Promise<Item> {
    return api.get<Item>(`/item/${id}`);
  },

  create(item: Item): Promise<Item> {
    return api.post<Item>('/item', item);
  },

  update(id: number, item: Item): Promise<Item> {
    return api.put<Item>(`/item/${id}`, item);
  },

  delete(id: number): Promise<void> {
    return api.delete<void>(`/item/${id}`);
  },

  /**
   * Attaches items to one parent in a single call: a set takes its pieces together, not one request
   * per piece. A 400 means the body names the parent itself, a repeated id or a cycle; a 404 means a
   * record is not in this organization, or is not under that parent; a 403 means the session may
   * not write. All three arrive as `ApiError`, like every other call in this file.
   */
  attachChildren(parentId: number, ids: number[]): Promise<MoveChildrenResult> {
    return api.post<MoveChildrenResult>(`/item/${parentId}/children`, { ids });
  },

  /**
   * Takes one piece out of a parent. The piece itself is untouched: it becomes a top-level item
   * again, which is why the inventory still lists it.
   */
  detachChild(parentId: number, childId: number): Promise<void> {
    return api.delete<void>(`/item/${parentId}/children/${childId}`);
  },

  /**
   * A node of the tree the response returned, or `undefined`. The tree is the only place a child is
   * stored, so anything that needs a node by id (the set's pieces) asks it and not the form.
   */
  findItem(nodes: Item[], id: number): Item | undefined {
    for (const node of nodes) {
      if (node.id === id) return node;
      const found = itemsService.findItem(node.children ?? [], id);
      if (found) return found;
    }
    return undefined;
  },

  /**
   * The items a set may take: the tree's own roots, minus the set itself. An item that already has a
   * parent is deliberately absent — attaching it would silently move it out of the set that owns it,
   * so the user has to remove it there first.
   */
  rootCandidates(tree: Item[], setId: number): Item[] {
    return tree.filter((node) => node.id !== setId);
  },

  flattenItems(items: Item[], parentPath: string = '', depth: number = 0): FlattenedItem[] {
    const result: FlattenedItem[] = [];

    // Sort by sortOrder if needed
    const sortedItems = [...items].sort((a, b) => a.label.localeCompare(b.label));

    for (const constant of sortedItems) {
      const hierarchicalLabel = parentPath ? `${parentPath}/${constant.label}` : constant.label;

      result.push({
        id: constant.id,
        label: constant.label,
        hierarchicalLabel,
        depth,
        originalItem: constant,
      });

      // Recursively process children
      if (constant.children && constant.children.length > 0) {
        const childResults = itemsService.flattenItems(
          constant.children,
          hierarchicalLabel,
          depth + 1,
        );
        result.push(...childResults);
      }
    }

    return result;
  },
};
