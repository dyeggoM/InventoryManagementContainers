import { computed, ref } from 'vue';
import {
  constantsService,
  codeOf,
  entryOf,
  type CatalogueEntry,
  type Constants,
} from '@services/constants';
import { prettyLabel } from '../services/utilities';

/**
 * The group whose rows define an item's attributes. A group is an identity as well, so this is a
 * code: the label of that row in Configuration can be «Atributo» without breaking the lookup.
 */
const ATTRIBUTE_GROUP_CODE = 'Attribute';

/**
 * The attribute half of Configuration: the attributes in sort order, how each one is edited, the
 * values each list allows, and the write that adds a value a list does not have yet.
 *
 * Every entry carries a **code** and a **label**. The code is what an item stores (the key of the
 * attributes map, the stored value of a list attribute); the label is what a screen shows. That
 * split is the whole point: renaming «Marca» in Configuration changes no item.
 *
 * Two screens need exactly this. The item form (`pages/index/inventory.vue`) has always read it, and
 * the import's row correction edits an item with the same fields, so it reads the same catalogue.
 * A second copy would be a second set of rules for one concern.
 *
 * `load` throws when the API refuses, because the caller owns the message. `createValue` never
 * throws — it answers `null` and the editor keeps its row, which is the contract `AttributeEditor`
 * was built against.
 */
export function useAttributeCatalogue(
  options: {
    /** The write landed but the refresh did not: the caller reports it without undoing the value. */
    onRefreshError?: (error: unknown) => void;
  } = {},
) {
  /** The attributes, in Configuration's order: code for the data, label for the screen. */
  const attributes = ref<CatalogueEntry[]>([]);
  /** attribute code -> data type (Text, Number, Money, Date, LongText), set in Configuration. */
  const types = ref<Record<string, string>>({});
  /** attribute code -> its allowed values, each a code with the label the select shows. */
  const lists = ref<Record<string, CatalogueEntry[]>>({});
  /** The `Attribute` constants themselves: each one is the parent of its own values. */
  const nodes = ref<Constants[]>([]);
  /** Every constant, flat: needed to place a new value after its siblings. */
  const panels = ref<Constants[]>([]);

  /** label -> code of every constant in the file, to resolve a group name the API sent. */
  const codesByLabel = computed(() => {
    const map = new Map<string, string>();
    for (const constant of panels.value) {
      if (constant.label && !map.has(constant.label)) map.set(constant.label, codeOf(constant));
    }
    return map;
  });

  /** code -> label, of the attributes and of every listed value: what a code looks like. */
  const labelsByCode = computed(() => {
    const map = new Map<string, string>();
    for (const entry of attributes.value) map.set(entry.code, entry.label);
    for (const values of Object.values(lists.value)) {
      for (const entry of values) if (!map.has(entry.code)) map.set(entry.code, entry.label);
    }
    return map;
  });

  /**
   * The group a value belongs to, as a code. The API may name a group by its code or by its label
   * (both were the same string until `Code` existed); the catalogue is what says which one it is,
   * so the lookup never depends on comparing a label against a known word.
   */
  function groupCode(group: string | null | undefined): string {
    if (!group) return '';
    return codesByLabel.value.get(group) ?? group;
  }

  /**
   * The label of a code. A code the catalogue does not know — a hand-typed attribute, one the
   * importer derives — is read as words, which is how it was always shown.
   */
  function labelOf(code: string): string {
    return labelsByCode.value.get(code) ?? (prettyLabel(code) || code);
  }

  /** The label of one value of one list; free text is its own label. */
  function labelOfValue(attributeCode: string, valueCode: string): string {
    const entry = (lists.value[attributeCode] ?? []).find((value) => value.code === valueCode);
    return entry?.label ?? labelOf(valueCode);
  }

  async function load(): Promise<void> {
    const [attributeRows, all] = await Promise.all([
      constantsService.getByGroup(ATTRIBUTE_GROUP_CODE),
      constantsService.getAll(),
    ]);

    nodes.value = attributeRows ?? [];
    attributes.value = (attributeRows ?? [])
      .toSorted((a, b) => a.sortOrder - b.sortOrder)
      .map(entryOf);

    // How each attribute is edited comes from its definition in Configuration.
    const dataTypes: Record<string, string> = {};
    for (const row of attributeRows ?? []) dataTypes[codeOf(row)] = row.dataType ?? 'Text';
    types.value = dataTypes;

    panels.value = all?.panels ?? [];

    // An attribute with values in the constants tree is a choice list; anything else is free text.
    const choices: Record<string, CatalogueEntry[]> = {};
    for (const constant of panels.value) {
      if (!constant.group || constant.parentId == null) continue;
      const key = groupCode(constant.group);
      (choices[key] ??= []).push(entryOf(constant));
    }
    for (const key of Object.keys(choices)) {
      choices[key] = (choices[key] ?? []).toSorted((a, b) => a.label.localeCompare(b.label));
    }
    lists.value = choices;
  }

  /** Where the new value goes when a list has no values yet. */
  function nextSortOrder(parentId: number): number {
    const siblings = panels.value.filter((constant) => constant.parentId === parentId);
    return (
      siblings.reduce((highest, constant) => Math.max(highest, constant.sortOrder ?? 0), 0) + 1
    );
  }

  /**
   * Saves a value the item form does not find in its list, so the user does not have to open
   * Configuration first. Only an admin or a superadmin is given this function; the API enforces the
   * same rule.
   *
   * It answers the entry the server stored — the code is the server's to assign, and the editor
   * must write that one, not the text the user typed.
   */
  async function createValue(attributeCode: string, label: string): Promise<CatalogueEntry | null> {
    const node = nodes.value.find((constant) => codeOf(constant) === attributeCode);
    const trimmed = label.trim();
    if (!node || !trimmed) return null;

    // The values of a list attribute hang from the catalogue node that owns the group (the values
    // of «Marca» under the `Brand` node), not from the `Attribute` definition that merely names the
    // attribute. Reuse the parent of a value that is already there; with none yet, the definition is
    // the least wrong place, and the group is what the editor actually reads.
    const sibling = panels.value.find(
      (constant) => groupCode(constant.group) === attributeCode && constant.parentId != null,
    );
    const parentId = sibling?.parentId ?? node.id;

    let created: Constants;
    try {
      created = await constantsService.create({
        id: 0,
        label: trimmed,
        icon: '',
        children: [],
        sortOrder: nextSortOrder(parentId),
        group: attributeCode,
        parentId,
        description: '',
        parent: undefined,
      });
    } catch {
      return null;
    }

    // The value is written: a failed refresh must not undo the row the user just finished.
    await load().catch((error: unknown) => options.onRefreshError?.(error));
    return entryOf(created);
  }

  return { attributes, types, lists, load, createValue, labelOf, labelOfValue };
}
