import { api } from './api';

export interface Attachment {
  id: number;
  itemId: number;
  type?: string | null;
  fileName?: string | null;
  filePath?: string | null;
  fileBase64?: string | null;
  attributes?: string | null;
}

export const attachmentsService = {
  getAll(): Promise<Attachment[]> {
    return api.get<Attachment[]>('/attachment');
  },

  getById(id: number): Promise<Attachment> {
    return api.get<Attachment>(`/attachment/${id}`);
  },

  getAllByItemId(id: number): Promise<Attachment[]> {
    return api.get<Attachment[]>(`/attachment/item/${id}`);
  },

  create(attachment: Attachment): Promise<Attachment> {
    return api.post<Attachment>('/attachment', attachment);
  },

  //   update(id: number, item: Attachment): Promise<Attachment> {
  //     return api.put<Attachment>(`/attachment/${id}`, item);
  //   },

  delete(id: number): Promise<void> {
    return api.delete<void>(`/attachment/${id}`);
  },
};
