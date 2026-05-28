export type LeafPane = {
  type: 'leaf';
  id: string;
  tabs: string[];
  activeTabId: string | null;
};

export type SplitDirection = 'horizontal' | 'vertical';

export type SplitPane = {
  type: 'split';
  id: string;
  direction: SplitDirection;
  children: PaneNode[];
  ratios: number[];
};

export type PaneNode = LeafPane | SplitPane;

export type SplitTarget = 'right' | 'down' | 'left' | 'up';

const generateId = () => {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID();
  }
  return `id-${Date.now()}-${Math.random().toString(16).slice(2)}`;
};

export const createLeaf = (tabs: string[] = [], activeTabId: string | null = null): LeafPane => ({
  type: 'leaf',
  id: generateId(),
  tabs,
  activeTabId
});

const findLeafById = (root: PaneNode, leafId: string): LeafPane | null => {
  if (root.type === 'leaf') {
    return root.id === leafId ? root : null;
  }
  for (const child of root.children) {
    const found = findLeafById(child, leafId);
    if (found) return found;
  }
  return null;
};

export const getLeaf = (root: PaneNode, leafId: string): LeafPane | null =>
  findLeafById(root, leafId);

export const collectLeaves = (root: PaneNode): LeafPane[] => {
  if (root.type === 'leaf') return [root];
  return root.children.flatMap(collectLeaves);
};

export const firstLeaf = (root: PaneNode): LeafPane => collectLeaves(root)[0];

const cloneRatios = (ratios: number[]): number[] => [...ratios];

const transformLeaf = (
  node: PaneNode,
  leafId: string,
  transform: (leaf: LeafPane) => PaneNode
): PaneNode => {
  if (node.type === 'leaf') {
    return node.id === leafId ? transform(node) : node;
  }

  const nextChildren = node.children.map((child) => transformLeaf(child, leafId, transform));
  const changed = nextChildren.some((child, index) => child !== node.children[index]);
  if (!changed) return node;

  return { ...node, children: nextChildren, ratios: cloneRatios(node.ratios) };
};

export const updateLeaf = (
  root: PaneNode,
  leafId: string,
  updater: (leaf: LeafPane) => LeafPane
): PaneNode => transformLeaf(root, leafId, updater);

export const openTabInLeaf = (
  root: PaneNode,
  leafId: string,
  noteId: string
): PaneNode =>
  updateLeaf(root, leafId, (leaf) => {
    const tabs = leaf.tabs.includes(noteId) ? leaf.tabs : [...leaf.tabs, noteId];
    return { ...leaf, tabs, activeTabId: noteId };
  });

export const setActiveTab = (
  root: PaneNode,
  leafId: string,
  noteId: string
): PaneNode =>
  updateLeaf(root, leafId, (leaf) =>
    leaf.tabs.includes(noteId) ? { ...leaf, activeTabId: noteId } : leaf
  );

export const closeTabInLeaf = (
  root: PaneNode,
  leafId: string,
  noteId: string
): PaneNode =>
  updateLeaf(root, leafId, (leaf) => {
    const tabs = leaf.tabs.filter((id) => id !== noteId);
    let activeTabId = leaf.activeTabId;
    if (activeTabId === noteId) {
      const removedIndex = leaf.tabs.indexOf(noteId);
      activeTabId = tabs[removedIndex] ?? tabs[removedIndex - 1] ?? tabs[0] ?? null;
    }
    return { ...leaf, tabs, activeTabId };
  });

export const moveTab = (
  root: PaneNode,
  sourceLeafId: string,
  noteId: string,
  destLeafId: string,
  destIndex: number
): PaneNode => {
  if (sourceLeafId === destLeafId) {
    return updateLeaf(root, destLeafId, (leaf) => {
      if (!leaf.tabs.includes(noteId)) return leaf;
      const filtered = leaf.tabs.filter((id) => id !== noteId);
      const insertAt = destIndex < 0 || destIndex > filtered.length ? filtered.length : destIndex;
      const tabs = [...filtered.slice(0, insertAt), noteId, ...filtered.slice(insertAt)];
      return { ...leaf, tabs, activeTabId: noteId };
    });
  }

  let next = updateLeaf(root, sourceLeafId, (leaf) => {
    if (!leaf.tabs.includes(noteId)) return leaf;
    const tabs = leaf.tabs.filter((id) => id !== noteId);
    let activeTabId = leaf.activeTabId;
    if (activeTabId === noteId) {
      const removedIndex = leaf.tabs.indexOf(noteId);
      activeTabId = tabs[removedIndex] ?? tabs[removedIndex - 1] ?? tabs[0] ?? null;
    }
    return { ...leaf, tabs, activeTabId };
  });

  next = updateLeaf(next, destLeafId, (leaf) => {
    const filtered = leaf.tabs.filter((id) => id !== noteId);
    const insertAt = destIndex < 0 || destIndex > filtered.length ? filtered.length : destIndex;
    const tabs = [...filtered.slice(0, insertAt), noteId, ...filtered.slice(insertAt)];
    return { ...leaf, tabs, activeTabId: noteId };
  });

  return next;
};

const splitDirectionFor = (target: SplitTarget): SplitDirection =>
  target === 'right' || target === 'left' ? 'horizontal' : 'vertical';

const splitOrder = (target: SplitTarget) =>
  target === 'right' || target === 'down' ? 'after' : 'before';

export const splitLeaf = (
  root: PaneNode,
  leafId: string,
  target: SplitTarget,
  initialTabs: string[] = []
): { tree: PaneNode; newLeafId: string } => {
  const newLeaf = createLeaf(initialTabs, initialTabs[initialTabs.length - 1] ?? null);
  const direction = splitDirectionFor(target);
  const order = splitOrder(target);

  const tree = transformLeaf(root, leafId, (leaf) => {
    const ordered = order === 'after' ? [leaf, newLeaf] : [newLeaf, leaf];
    return {
      type: 'split',
      id: generateId(),
      direction,
      children: ordered,
      ratios: [0.5, 0.5]
    };
  });

  return { tree, newLeafId: newLeaf.id };
};

const collapseSplit = (split: SplitPane): PaneNode => {
  if (split.children.length === 1) {
    return split.children[0];
  }
  return split;
};

const removeLeafRecursive = (
  node: PaneNode,
  leafId: string
): PaneNode | null => {
  if (node.type === 'leaf') {
    return node.id === leafId ? null : node;
  }

  const nextChildren: PaneNode[] = [];
  const keptIndices: number[] = [];
  node.children.forEach((child, index) => {
    const replacement = removeLeafRecursive(child, leafId);
    if (replacement) {
      nextChildren.push(replacement);
      keptIndices.push(index);
    }
  });

  if (nextChildren.length === 0) return null;

  const nextRatios = keptIndices.map((index) => node.ratios[index] ?? 1 / node.children.length);
  const total = nextRatios.reduce((sum, value) => sum + value, 0) || 1;
  const normalized = nextRatios.map((value) => value / total);

  const next: SplitPane = {
    ...node,
    children: nextChildren,
    ratios: normalized
  };

  return collapseSplit(next);
};

export const closeLeaf = (root: PaneNode, leafId: string): PaneNode | null =>
  removeLeafRecursive(root, leafId);

export const setSplitRatios = (
  root: PaneNode,
  splitId: string,
  ratios: number[]
): PaneNode => {
  if (root.type === 'leaf') return root;

  if (root.id === splitId && root.ratios.length === ratios.length) {
    return { ...root, ratios };
  }

  const nextChildren = root.children.map((child) => setSplitRatios(child, splitId, ratios));
  const changed = nextChildren.some((child, index) => child !== root.children[index]);
  if (!changed) return root;

  return { ...root, children: nextChildren, ratios: cloneRatios(root.ratios) };
};

export const removeNoteFromTree = (root: PaneNode, noteId: string): PaneNode | null => {
  if (root.type === 'leaf') {
    if (!root.tabs.includes(noteId)) return root;
    const tabs = root.tabs.filter((id) => id !== noteId);
    let activeTabId = root.activeTabId;
    if (activeTabId === noteId) {
      const removedIndex = root.tabs.indexOf(noteId);
      activeTabId = tabs[removedIndex] ?? tabs[removedIndex - 1] ?? tabs[0] ?? null;
    }
    return { ...root, tabs, activeTabId };
  }

  const nextChildren: PaneNode[] = [];
  const keptIndices: number[] = [];
  root.children.forEach((child, index) => {
    const replacement = removeNoteFromTree(child, noteId);
    if (replacement) {
      nextChildren.push(replacement);
      keptIndices.push(index);
    }
  });

  if (nextChildren.length === 0) return null;

  const nextRatios = keptIndices.map((index) => root.ratios[index] ?? 1 / root.children.length);
  const total = nextRatios.reduce((sum, value) => sum + value, 0) || 1;
  const normalized = nextRatios.map((value) => value / total);

  return collapseSplit({
    ...root,
    children: nextChildren,
    ratios: normalized
  });
};

export const ensureLeaf = (root: PaneNode | null): PaneNode => {
  if (root) return root;
  return createLeaf();
};

export const isLeaf = (node: PaneNode): node is LeafPane => node.type === 'leaf';

/**
 * Pull a tab out of one leaf and create a new split next to a destination
 * leaf, opening the moved tab in the new leaf. Cleans up the source leaf
 * if it becomes empty.
 */
export const moveTabToSplit = (
  root: PaneNode,
  sourceLeafId: string,
  noteId: string,
  destLeafId: string,
  target: SplitTarget
): { tree: PaneNode; newLeafId: string } => {
  let next: PaneNode = root;

  // 1. Remove the tab from the source (if present).
  next = closeTabInLeaf(next, sourceLeafId, noteId);

  // 2. Split the destination, seeding the new leaf with the moved tab.
  const result = splitLeaf(next, destLeafId, target, [noteId]);
  next = result.tree;

  // 3. If the source leaf is now empty (and isn't the dest leaf), drop it.
  if (sourceLeafId !== destLeafId) {
    const stillThere = getLeaf(next, sourceLeafId);
    if (stillThere && stillThere.tabs.length === 0) {
      const cleaned = closeLeaf(next, sourceLeafId);
      if (cleaned) next = cleaned;
    }
  }

  return { tree: next, newLeafId: result.newLeafId };
};
