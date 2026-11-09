import { api } from './api';

export interface Constants {
  id: number;
  label: string;
  icon: string;
  children: Constants[];
  sortOrder: number;
  group?: string | undefined;
  parentId?: number | null | undefined;
  description?: string | undefined;
  parent?: Constants | undefined;
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

  //   delete(id: number): Promise<void> {
  //     return api.delete<void>(`/constants/${id}`);
  //   },

  flattenConstants(
    constants: Constants[],
    parentPath: string = '',
    depth: number = 0,
  ): FlattenedConstant[] {
    const result: FlattenedConstant[] = [];

    // Sort by sortOrder if needed
    const sortedConstants = [...constants].sort((a, b) => a.sortOrder - b.sortOrder);

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
