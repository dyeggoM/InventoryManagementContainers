import { api } from './api';

export interface Attachment {
  id: number;
  itemId: number;
  type?: string | null;
  fileName?: string | null;
  filePath?: string | null;
  /** Small compressed preview (data URL); the original lives on the API's disk. */
  thumbnailBase64?: string | null;
  /** Input only: the picked file as a data URL. Stored to disk by the API. */
  uploadBase64?: string | null;
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

  /** Uploads the file as bytes: no base64, no third of the size in escaping. */
  upload(itemId: number, file: File, type?: string | null): Promise<Attachment> {
    const form = new FormData();
    form.append('itemId', String(itemId));
    if (type) form.append('type', type);
    form.append('fileName', file.name);
    form.append('file', file, file.name);
    return api.postForm<Attachment>('/attachment/upload', form);
  },

  //   update(id: number, item: Attachment): Promise<Attachment> {
  //     return api.put<Attachment>(`/attachment/${id}`, item);
  //   },

  delete(id: number): Promise<void> {
    return api.delete<void>(`/attachment/${id}`);
  },

  /**
   * The original file as bytes, signed with the session token. The route requires read access, so a
   * plain link answers 401: every read of the original goes through here.
   */
  fetchFile(id: number): Promise<Blob> {
    return api.getBlob(`/attachment/${id}/file`);
  },
};
