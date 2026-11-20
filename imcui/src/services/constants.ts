import { api } from './api';

export interface Constants {
  id: number;
  /**
   * Stable identity: what an item stores as an attribute key or as a list value. The API assigns
   * it and never rewrites it, so a rename changes `label` only. Rows that predate the column
   * arrive without it; `codeOf` falls back to the label for exactly those.
   */
  code?: string | null | undefined;
  /** What the screen shows. Translating it is translating copy, not data. */
  label: string;
  icon: string;
  children: Constants[];
  sortOrder: number;
  group?: string | undefined;
  parentId?: number | null | undefined;
  description?: string | undefined;
  /** How an attribute value is edited: Text | Number | Money | Date | LongText. */
  dataType?: string | null | undefined;
  parent?: Constants | undefined;
}

/** One entry of the catalogue: the code is the data, the label is what the screen shows. */
export interface CatalogueEntry {
  code: string;
  label: string;
}

/**
 * The code of a constant. The migration fills `Code := Label` for the rows that predate the
 * column, and those are the strings the items already store, so the label is the right fallback —
 * never a translation, always the identity.
 */
export function codeOf(constant: Constants): string {
  return (constant.code ?? '').trim() || constant.label;
}

/** The catalogue entry of a constant: identity and display text, side by side. */
export function entryOf(constant: Constants): CatalogueEntry {
  return { code: codeOf(constant), label: constant.label || codeOf(constant) };
}

export interface ConstantsReponse {
  tree: Constants[];
  panels: Constants[];
}

export interface FlattenedConstant {
  id: number;
  label: string;
  hierarchicalLabel: string;
  depth: number;
  originalConstant: Constants;
}

export const constantsService = {
  getAll(): Promise<ConstantsReponse> {
    return api.get<ConstantsReponse>('/constants');
  },

  getById(id: number): Promise<Constants> {
    return api.get<Constants>(`/constants/${id}`);
  },

  getByGroup(group: string): Promise<Constants[]> {
    return api.get<Constants[]>(`/constants/group/${group}`);
  },

  create(constant: Constants): Promise<Constants> {
    return api.post<Constants>('/constants', constant);
  },

  update(id: number, constant: Constants): Promise<Constants> {
    return api.put<Constants>(`/constants/${id}`, constant);
  },

  delete(id: number): Promise<void> {
    return api.delete<void>(`/constants/${id}`);
  },

  flattenConstants(
    constants: Constants[],
    parentPath: string = '',
    depth: number = 0,
  ): FlattenedConstant[] {
    const result: FlattenedConstant[] = [];

    // Sort by sortOrder if needed
    const sortedConstants = constants.toSorted((a, b) => a.sortOrder - b.sortOrder);

    for (const constant of sortedConstants) {
      const hierarchicalLabel = parentPath ? `${parentPath}/${constant.label}` : constant.label;

      result.push({
        id: constant.id,
        label: constant.label,
        hierarchicalLabel,
        depth,
        originalConstant: constant,
      });

      // Recursively process children
      if (constant.children && constant.children.length > 0) {
        const childResults = constantsService.flattenConstants(
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
