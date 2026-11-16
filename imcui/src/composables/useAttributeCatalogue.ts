import { ref } from 'vue';
import { constantsService, type Constants } from '@services/constants';

/**
 * The attribute half of Configuration: the labels in sort order, how each one is edited, the values
 * each list allows, and the write that adds a value a list does not have yet.
 *
 * Two screens need exactly this. The item form (`pages/index/inventory.vue`) has always read it, and
 * the import's row correction edits an item with the same fields, so it reads the same catalogue.
 * A second copy would be a second set of rules for one concern.
 *
 * `load` throws when the API refuses, because the caller owns the message: the item form says it in
 * English, the import panel in Spanish. `createValue` never throws — it answers `false` and the
 * editor keeps its row, which is the contract `AttributeEditor` was built against.
 */
export function useAttributeCatalogue(
  options: {
    /** The write landed but the refresh did not: the caller reports it without undoing the value. */
    onRefreshError?: (error: unknown) => void;
  } = {},
) {
  /** Attribute labels from the `Attribute` constant, in the order Configuration gives them. */
  const labels = ref<string[]>([]);
  /** label -> data type (Text, Number, Money, Date, LongText), set in Configuration. */
  const types = ref<Record<string, string>>({});
  /** label -> allowed values, taken from the constants whose group is that label. */
  const lists = ref<Record<string, string[]>>({});
  /** The `Attribute` constants themselves: each one is the parent of its own values. */
  const nodes = ref<Constants[]>([]);
  /** Every constant, flat: needed to place a new value after its siblings. */
  const panels = ref<Constants[]>([]);

  async function load(): Promise<void> {
    const [attributeRows, all] = await Promise.all([
      constantsService.getByGroup('Attribute'),
      constantsService.getAll(),
    ]);

    nodes.value = attributeRows ?? [];
    labels.value = (attributeRows ?? [])
      .slice()
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map((row) => row.label);

    // How each attribute is edited comes from its definition in Configuration.
    const dataTypes: Record<string, string> = {};
    for (const row of attributeRows ?? []) dataTypes[row.label] = row.dataType ?? 'Text';
    types.value = dataTypes;

    // A label with children in the constants tree is a choice list; anything else is free text.
    const choices: Record<string, string[]> = {};
    for (const constant of all?.panels ?? []) {
      if (!constant.group || constant.parentId == null) continue;
      const bucket = choices[constant.group] ?? [];
      bucket.push(constant.label);
      choices[constant.group] = bucket;
    }
    for (const group of Object.keys(choices)) {
      choices[group] = (choices[group] ?? []).sort((a, b) => a.localeCompare(b));
    }
    lists.value = choices;
    panels.value = all?.panels ?? [];
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
   */
  async function createValue(label: string, value: string): Promise<boolean> {
    const node = nodes.value.find((constant) => constant.label === label);
    const trimmed = value.trim();
    if (!node || !trimmed) return false;

    // The values of a list attribute hang from the catalogue node that owns the group (`Brand`
    // values under the `Brand` node), not from the `Attribute` definition that merely names the
    // attribute. Reuse the parent of a value that is already there; with none yet, the definition is
    // the least wrong place, and the group is what the editor actually reads.
    const sibling = panels.value.find(
      (constant) => constant.group === label && constant.parentId != null,
    );
    const parentId = sibling?.parentId ?? node.id;

    try {
      await constantsService.create({
        id: 0,
        label: trimmed,
        icon: '',
        children: [],
        sortOrder: nextSortOrder(parentId),
        group: label,
        parentId,
        description: '',
        parent: undefined,
      });
    } catch {
      return false;
    }

    // The value is written: a failed refresh must not undo the row the user just finished.
    await load().catch((error: unknown) => options.onRefreshError?.(error));
    return true;
  }

  return { labels, types, lists, load, createValue };
}
