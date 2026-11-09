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
